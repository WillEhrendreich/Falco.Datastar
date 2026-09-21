namespace Falco.Datastar

open System
open System.Text
open System.Text.RegularExpressions

module internal String =
    let newLines = [| "\r\n"; "\n"; "\r" |]

    /// Datastar's own kebab (library/src/utils/text.ts), which Rocket uses to turn a prop name into its attribute name.
    /// Unlike toKebab it splits acronyms and digit boundaries: "innerHTML" -> "inner-html", "pos3d" -> "pos-3-d".
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

/// Builds JavaScript literals that are safe to place inside a double-quoted HTML attribute.
/// Falco.Markup does not escape attribute values, so anything embedded in an expression has to be escaped here.
module internal Js =
    let attrEncode (value:string) =
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

    let stringLiteral (value:string) =
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r")
        |> attrEncode
        |> fun escaped -> "'" + escaped + "'"

    /// camelCase, like the JavaScript objects Rocket props decode into. Shared because building options per call is costly.
    let webJsonOptions = System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)

    /// Strings become single-quoted literals; everything else is its JSON form (true, 5, 1.5, null)
    let literal<'T> (value:'T) =
        match box value with
        | :? string as text -> stringLiteral text
        | _ -> System.Text.Json.JsonSerializer.Serialize<'T>(value) |> attrEncode

module internal Bool =
    let inline eitherOr trueThing falseThing bool =
        match bool with
        | true -> trueThing
        | _ -> falseThing

module Option =
    let toValueOption = function
        | Some value -> ValueSome value
        | None -> ValueNone
