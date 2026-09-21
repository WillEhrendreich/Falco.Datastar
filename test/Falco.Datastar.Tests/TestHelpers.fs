namespace Falco.Datastar.Tests

open System.Globalization
open Falco.Markup

[<AutoOpen>]
module TestHelpers =
    /// An attribute on a div, as the HTML it renders
    let renderAttr attr =
        Elem.div [ attr ] []
        |> renderNode

    /// Runs the function in a culture that writes 1.5 as 1,5, and puts the old culture back afterwards.
    /// The culture is built from the invariant one so the tests do not need ICU, which slim CI images lack.
    let withCommaDecimalCulture (run: unit -> unit) =
        let commaDecimal = CultureInfo.InvariantCulture.Clone() :?> CultureInfo
        commaDecimal.NumberFormat.NumberDecimalSeparator <- ","
        let original = CultureInfo.CurrentCulture
        try
            CultureInfo.CurrentCulture <- commaDecimal
            run ()
        finally
            CultureInfo.CurrentCulture <- original
