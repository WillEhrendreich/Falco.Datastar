namespace Falco.Datastar

open System
open System.Text

module internal String =
    let newLines = [| "\r\n"; "\n"; "\r" |]
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
