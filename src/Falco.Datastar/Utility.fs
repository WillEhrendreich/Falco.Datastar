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

module internal Bool =
    let inline eitherOr trueThing falseThing bool =
        match bool with
        | true -> trueThing
        | _ -> falseThing

module Option =
    let toValueOption = function
        | Some value -> ValueSome value
        | None -> ValueNone
