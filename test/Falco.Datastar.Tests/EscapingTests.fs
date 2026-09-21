namespace Falco.Datastar.Tests

open System
open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

// The string helpers put text into a JavaScript expression inside an HTML attribute.
// Anything from a user has to stay text, and states that Datastar cannot use must not be writable.
module EscapingTests =
    let private renderAttr attr =
        Elem.div [ attr ] []
        |> renderNode

    [<Fact>]
    let ``A URL cannot break out of its quotes in a backend action`` () =
        Ds.get "/x');alert(1);//" |> should equal """@get('/x\');alert(1);//')"""
        Ds.post "/x');alert(1);//" |> should equal """@post('/x\');alert(1);//')"""
        Ds.delete "/x');alert(1);//" |> should equal """@delete('/x\');alert(1);//')"""

    [<Fact>]
    let ``A URL with an ampersand stays one URL after the browser reads the attribute`` () =
        Ds.get "/items?a=1&b=2" |> should equal "@get('/items?a=1&amp;b=2')"

    [<Fact>]
    let ``A text value of Ds.signal is escaped`` () =
        renderAttr (Ds.signal (SignalPath.sp "a", "it's \" <b>"))
        |> should equal """<div data-signals:a="'it\'s &quot; &lt;b&gt;'"></div>"""

    [<Fact>]
    let ``A structured value of Ds.signal cannot break out of the attribute`` () =
        renderAttr (Ds.signal (SignalPath.sp "a", {| name = "x\"y" |}))
        |> should equal """<div data-signals:a="{&quot;name&quot;:&quot;x\u0022y&quot;}"></div>"""

    [<Fact>]
    let ``Numbers and booleans in Ds.signal are unchanged`` () =
        renderAttr (Ds.signal (SignalPath.sp "a", 5)) |> should equal """<div data-signals:a="5"></div>"""
        renderAttr (Ds.signal (SignalPath.sp "a", true)) |> should equal """<div data-signals:a="true"></div>"""

    [<Fact>]
    let ``A number that is not finite is a JavaScript literal and does not raise`` () =
        Ds.setAll ("a.", Double.NaN) |> should equal """@setAll(NaN, { include: /^a\./ })"""
        Ds.setAll ("a.", Double.PositiveInfinity) |> should equal """@setAll(Infinity, { include: /^a\./ })"""
        Ds.setAll ("a.", Double.NegativeInfinity) |> should equal """@setAll(-Infinity, { include: /^a\./ })"""

    [<Fact>]
    let ``Ds.nonce refuses an empty nonce, because Datastar stops at load when the nonce is empty`` () =
        let raised = Assert.Throws<ArgumentException>(fun () -> Ds.nonce "" |> ignore)
        raised.Message |> should haveSubstring "NonceRequired"
        Assert.Throws<ArgumentException>(fun () -> Ds.nonce "   " |> ignore) |> ignore

    [<Fact>]
    let ``Ds.bindEvent needs at least one event, because Datastar would never sync an empty list`` () =
        renderAttr (Ds.bindEvent (SignalPath.sp "val", "input"))
        |> should equal """<div data-bind:val__event.input></div>"""
        renderAttr (Ds.bindEvent (SignalPath.sp "val", "input", [ "change" ]))
        |> should equal """<div data-bind:val__event.input.change></div>"""

    [<Fact>]
    let ``Ds.bindEvent and Ds.bindProp refuse an empty name`` () =
        Assert.Throws<ArgumentException>(fun () -> Ds.bindEvent (SignalPath.sp "val", " ") |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> Ds.bindProp (SignalPath.sp "val", "") |> ignore) |> ignore
