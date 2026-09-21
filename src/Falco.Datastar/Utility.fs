namespace Falco.Datastar

open System
open System.Buffers
open System.Globalization
open System.Text
open System.Text.RegularExpressions

module internal String =
    let newLines = [| "\r\n"; "\n"; "\r" |]

    /// A copy of Datastar's own kebab function (library/src/utils/text.ts). Rocket uses it to turn a prop name into an attribute name.
    /// Unlike toKebab, it also splits acronyms and digits: "innerHTML" becomes "inner-html" and "pos3d" becomes "pos-3-d".
    let computeDatastarKebab (value:string) =
        let replace (pattern:string) (replacement:string) (options:RegexOptions) (input:string) =
            Regex.Replace(input, pattern, replacement, options)
        value
        |> replace "([A-Z]+)([A-Z][a-z])" "$1-$2" RegexOptions.None
        |> replace "([a-z0-9])([A-Z])" "$1-$2" RegexOptions.None
        |> replace "([a-z])([0-9]+)" "$1-$2" RegexOptions.IgnoreCase
        |> replace "([0-9]+)([a-z])" "$1-$2" RegexOptions.IgnoreCase
        |> replace "[\\s_]+" "-" RegexOptions.None
        |> fun kebab -> kebab.ToLowerInvariant()

    /// Prop names are written in code and repeat on every render, so each one is worked out once. The cache stops growing at 1000 names, in case a name ever comes from user input.
    let kebabCache = System.Collections.Concurrent.ConcurrentDictionary<string, string>()

    let datastarKebab (value:string) =
        match kebabCache.TryGetValue value with
        | true, kebab -> kebab
        | false, _ ->
            let kebab = computeDatastarKebab value
            if kebabCache.Count < 1000 then kebabCache.TryAdd(value, kebab) |> ignore
            kebab
    let split (delimiters:string seq) (line:string) = line.Split(delimiters |> Seq.toArray, StringSplitOptions.None)
    let IsPopulated = String.IsNullOrWhiteSpace >> not
    let toKebab (pascalString:string) =
        (StringBuilder(), pascalString.ToCharArray())
        ||> Seq.fold (fun stringBuilder chr ->
            if Char.IsUpper(chr)
            then stringBuilder.Append("-").Append(Char.ToLower(chr))
            else stringBuilder.Append(chr)
            )
        |> _.Replace("-", "", 0, 1).ToString()

/// Builds JavaScript literals that are safe inside a double-quoted HTML attribute.
/// Falco.Markup does not escape attribute values, so anything put into an expression must be escaped here.
module internal Js =
    /// The characters that need escaping in an attribute, and in a single-quoted JavaScript string inside one.
    /// Most text has none of them, and then it is returned as it is, without a copy.
    let attributeSpecials = SearchValues.Create "&<>\""
    let stringSpecials = SearchValues.Create "\\'\n\r&<>\""

    let attrEncode (value:string) =
        match value.AsSpan().IndexOfAny attributeSpecials with
        | -1 -> value
        | first ->
            let builder = StringBuilder(value.Length + 16).Append(value, 0, first)
            for index in first .. value.Length - 1 do
                match value.[index] with
                | '&' -> builder.Append "&amp;" |> ignore
                | '<' -> builder.Append "&lt;" |> ignore
                | '>' -> builder.Append "&gt;" |> ignore
                | '"' -> builder.Append "&quot;" |> ignore
                | other -> builder.Append other |> ignore
            builder.ToString()

    let stringLiteral (value:string) =
        match value.AsSpan().IndexOfAny stringSpecials with
        | -1 -> String.Concat("'", value, "'")
        | first ->
            let builder = StringBuilder(value.Length + 18).Append('\'').Append(value, 0, first)
            for index in first .. value.Length - 1 do
                match value.[index] with
                | '\\' -> builder.Append "\\\\" |> ignore
                | '\'' -> builder.Append "\\'" |> ignore
                | '\n' -> builder.Append "\\n" |> ignore
                | '\r' -> builder.Append "\\r" |> ignore
                | '&' -> builder.Append "&amp;" |> ignore
                | '<' -> builder.Append "&lt;" |> ignore
                | '>' -> builder.Append "&gt;" |> ignore
                | '"' -> builder.Append "&quot;" |> ignore
                | other -> builder.Append other |> ignore
            builder.Append('\'').ToString()

    /// A regular expression source as a JavaScript regular expression literal, such as /a\/b/. A literal cannot hold a slash or a line break as they are,
    /// so they are escaped. A backslash and the character after it are kept together, so an escape that is already there stays as it is.
    /// The text is not encoded for an attribute.
    let regexLiteral (pattern:string) =
        let builder = StringBuilder(pattern.Length + 4).Append('/')
        let mutable afterBackslash = false
        for character in pattern do
            match afterBackslash, character with
            | true, '\n' -> builder.Append 'n' |> ignore; afterBackslash <- false
            | true, '\r' -> builder.Append 'r' |> ignore; afterBackslash <- false
            | true, other -> builder.Append other |> ignore; afterBackslash <- false
            | false, '\\' -> builder.Append '\\' |> ignore; afterBackslash <- true
            | false, '/' -> builder.Append "\\/" |> ignore
            | false, '\n' -> builder.Append "\\n" |> ignore
            | false, '\r' -> builder.Append "\\r" |> ignore
            | false, '\u2028' -> builder.Append "\\u2028" |> ignore
            | false, '\u2029' -> builder.Append "\\u2029" |> ignore
            | false, other -> builder.Append other |> ignore
        if afterBackslash then
            raise (ArgumentException($"The pattern '{pattern}' ends with a backslash, so it is not a regular expression. Remove the backslash, or write two of them to match a backslash."))
        builder.Append('/').ToString()

    /// A regular expression source that goes in a JSON string. Datastar removes a slash at the start and at the end of a string pattern,
    /// as if it were a literal like /a/, so a slash that is part of the pattern is protected by an empty group.
    let regexString (pattern:string) =
        let start = match pattern.StartsWith '/' with | true -> "(?:)" | false -> ""
        let finish = match pattern.EndsWith '/' with | true -> "(?:)" | false -> ""
        start + pattern + finish

    /// camelCase names, to match the JavaScript objects Rocket props are read into. This is one shared instance, because creating options on every call is slow.
    let webJsonOptions = System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)

    /// A JavaScript number. NaN and the infinities are literals in JavaScript, so this never raises, and it always uses a dot.
    let number (value:float) =
        match Double.IsNaN value, Double.IsPositiveInfinity value, Double.IsNegativeInfinity value with
        | true, _, _ -> "NaN"
        | _, true, _ -> "Infinity"
        | _, _, true -> "-Infinity"
        | _ -> value.ToString("R", CultureInfo.InvariantCulture)

    /// Strings become single-quoted literals. Numbers, including NaN and the infinities, and booleans are written as they are. Everything else is written as JSON
    let literal<'T> (value:'T) =
        match box value with
        | :? string as text -> stringLiteral text
        | :? float as decimalNumber -> number decimalNumber
        | :? float32 as singleNumber when Single.IsFinite singleNumber -> singleNumber.ToString("R", CultureInfo.InvariantCulture)
        | :? float32 as singleNumber -> number (float singleNumber)
        | _ -> System.Text.Json.JsonSerializer.Serialize<'T>(value) |> attrEncode

/// Refuses input that Datastar cannot use, with a message that says what to do
module internal Guard =
    let notBlank (parameter:string) (message:string) (value:string) =
        if String.IsNullOrWhiteSpace value then raise (ArgumentException(message, parameter))

    /// Refuses text that is not a JavaScript identifier, such as item or $row. <c>message</c> says what was expected, and the text that was given is added to it.
    let javaScriptIdentifier (parameter:string) (message:string) (value:string) =
        if isNull value || not (Regex.IsMatch(value, @"^[A-Za-z_$][A-Za-z0-9_$]*\z")) then
            raise (ArgumentException($"{message}, but it is '{value}'.", parameter))

    /// Refuses a name that cannot go into the name of an attribute as it is. Falco.Markup does not escape attribute names, so a quote or a space
    /// would end the name early and could add attributes of its own. Datastar reads a double underscore as the start of a modifier.
    /// <c>what</c> says what the name is for, such as "class name". <c>hasModifiers</c> is true when modifiers follow the name.
    let attributeName (what:string) (hasModifiers:bool) (name:string) =
        let fail (reason:string) (remedy:string) =
            raise (ArgumentException($"The {what} '{name}' cannot be used in a data- attribute name, because {reason}. {remedy}"))
        if String.IsNullOrWhiteSpace name then
            fail "it is empty" "Write a name."
        match name |> Seq.tryFind (fun character -> Char.IsWhiteSpace character || Char.IsControl character || "\"'`<>/=".Contains character) with
        | Some character ->
            let described = match Char.IsWhiteSpace character || Char.IsControl character with | true -> "whitespace or a control character" | false -> $"'{character}'"
            fail $"it contains {described}" "HTML ends an attribute name there, and what follows would become attributes of their own. Use letters, digits, '-', '.' and ':'."
        | None -> ()
        match name.IndexOf("__", StringComparison.Ordinal) with
        | -1 -> ()
        | position ->
            let classHint = match what with | "class name" -> " To toggle a class like this, write the object form yourself, for example Attr.create \"data-class\" \"{'card__title': $isActive}\"." | _ -> ""
            fail "it contains '__'" $"Datastar reads '__' in an attribute name as the start of a modifier, so it would read '{name}' as '{name.Substring(0, position)}' with the modifier '{name.Substring(position + 2)}'. Choose a name without '__'.{classHint}"
        if hasModifiers && name.EndsWith '_' then
            fail "it ends with an underscore next to a modifier" "Datastar would read the underscore together with the two that start the modifier, and lose part of the name. Remove the trailing underscore."

module internal Bool =
    let inline eitherOr trueThing falseThing bool =
        match bool with
        | true -> trueThing
        | _ -> falseThing

module Option =
    let toValueOption = function
        | Some value -> ValueSome value
        | None -> ValueNone
