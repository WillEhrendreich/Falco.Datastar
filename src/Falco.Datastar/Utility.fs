namespace Falco.Datastar

open System
open System.Globalization
open System.Text
open System.Text.RegularExpressions

module internal String =
    let newLines = [| "\r\n"; "\n"; "\r" |]

    /// A copy of Datastar's own kebab function (library/src/utils/text.ts). Rocket uses it to turn a prop name into an attribute name.
    /// Unlike toKebab, it also splits acronyms and digits: "innerHTML" becomes "inner-html" and "pos3d" becomes "pos-3-d".
    let datastarKebab (value:string) =
        let replace (pattern:string) (replacement:string) (options:RegexOptions) (input:string) =
            Regex.Replace(input, pattern, replacement, options)
        value
        |> replace "([A-Z]+)([A-Z][a-z])" "$1-$2" RegexOptions.None
        |> replace "([a-z0-9])([A-Z])" "$1-$2" RegexOptions.None
        |> replace "([a-z])([0-9]+)" "$1-$2" RegexOptions.IgnoreCase
        |> replace "([0-9]+)([a-z])" "$1-$2" RegexOptions.IgnoreCase
        |> replace "[\\s_]+" "-" RegexOptions.None
        |> fun kebab -> kebab.ToLowerInvariant()
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
    let attrEncode (value:string) =
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

    let stringLiteral (value:string) =
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r")
        |> attrEncode
        |> fun escaped -> "'" + escaped + "'"

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
