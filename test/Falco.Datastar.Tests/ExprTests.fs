namespace Falco.Datastar.Tests

open System
open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

// Typed signals, expressions and statements. They replace the JavaScript strings in Ds.text, Ds.show, Ds.onClick and the like.
// The comments name the rule of the Tao of Datastar that a case protects.
module ExprTests =
    let private renderAttr attr =
        Elem.div [ attr ] []
        |> renderNode

    let private count = Signal.browser<int> "count"
    let private menuOpen = Signal.browser<bool> "menuOpen"
    let private firstName = Signal.server<string> "form.firstName"
    let private isOn = Signal.rocket<bool> "on"

    // Signals: where a signal lives decides whether Datastar sends it to the server

    [<Fact>]
    let ``A browser signal is written with the underscore that keeps it out of requests`` () =
        Signal.path count |> should equal "_count"
        Expr.toString (Expr.read count) |> should equal "$_count"

    [<Fact>]
    let ``A server signal keeps its name, so Datastar sends it with requests`` () =
        Signal.path firstName |> should equal "form.firstName"
        Expr.toString (Expr.read firstName) |> should equal "$form.firstName"

    [<Fact>]
    let ``A Rocket component signal is read with two dollar signs, as Rocket expects`` () =
        Expr.toString (Expr.read isOn) |> should equal "$$on"

    [<Fact>]
    let ``A dotted browser signal keeps the whole group out of requests`` () =
        Signal.path (Signal.browser<string> "form.tab") |> should equal "_form.tab"

    [<Fact>]
    let ``A signal remembers where it lives`` () =
        Signal.scope count |> should equal SignalScope.Browser
        Signal.scope firstName |> should equal SignalScope.Server
        Signal.scope isOn |> should equal SignalScope.RocketComponent

    [<Fact>]
    let ``A server signal cannot start with an underscore, because Datastar would keep it in the browser`` () =
        match Signal.tryCreate<string> SignalScope.Server "_secret" with
        | Error message ->
            message |> should haveSubstring "_secret"
            message |> should haveSubstring "Signal.browser"
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``A name with a hyphen is refused, because an expression reads it as minus`` () =
        match Signal.tryCreate<int> SignalScope.Browser "my-count" with
        | Error message -> message |> should haveSubstring "myCount"
        | Ok _ -> failwith "expected an error"

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("  ")>]
    [<InlineData("1abc")>]
    [<InlineData("a b")>]
    [<InlineData("a..b")>]
    [<InlineData("a.")>]
    [<InlineData("$a")>]
    let ``A name that Datastar cannot read as a signal path is refused`` (name: string) =
        match Signal.tryCreate<int> SignalScope.Browser name with
        | Error _ -> ()
        | Ok _ -> failwith $"expected an error for '{name}'"

    [<Fact>]
    let ``Signal.browser raises with the same message when the name is invalid`` () =
        let raised = Assert.Throws<ArgumentException>(fun () -> Signal.browser<int> "my-count" |> ignore)
        raised.Message |> should haveSubstring "myCount"

    // Expressions

    [<Fact>]
    let ``Literals are written as JavaScript literals`` () =
        Expr.toString (Expr.int 5) |> should equal "5"
        Expr.toString (Expr.int -3) |> should equal "(-3)"
        Expr.toString (Expr.float 1.5) |> should equal "1.5"
        Expr.toString (Expr.bool true) |> should equal "true"
        Expr.toString (Expr.string "hi") |> should equal "'hi'"

    [<Fact>]
    let ``A float is written with a dot whatever the current culture is`` () =
        let commaDecimal = Globalization.CultureInfo.InvariantCulture.Clone() :?> Globalization.CultureInfo
        commaDecimal.NumberFormat.NumberDecimalSeparator <- ","
        let original = Globalization.CultureInfo.CurrentCulture
        try
            Globalization.CultureInfo.CurrentCulture <- commaDecimal
            Expr.toString (Expr.float 1.5) |> should equal "1.5"
        finally
            Globalization.CultureInfo.CurrentCulture <- original

    [<Fact>]
    let ``A float that is not a finite number is still a JavaScript literal`` () =
        Expr.toString (Expr.float Double.NaN) |> should equal "NaN"
        Expr.toString (Expr.float Double.PositiveInfinity) |> should equal "Infinity"
        Expr.toString (Expr.float Double.NegativeInfinity) |> should equal "(-Infinity)"

    [<Fact>]
    let ``A string literal cannot break out of its quotes or of the attribute`` () =
        Expr.toString (Expr.string "it's \"q\" <b>")
        |> should equal """'it\'s &quot;q&quot; &lt;b&gt;'"""
        Expr.toString (Expr.string "x');alert(1);//")
        |> should equal """'x\');alert(1);//'"""

    [<Fact>]
    let ``Arithmetic is written with spaces, because $a-1 would be read as a signal called a-1`` () =
        Expr.toString (Expr.subtract (Expr.read count) (Expr.int 1)) |> should equal "($_count - 1)"
        Expr.toString (Expr.add (Expr.read count) (Expr.int 1)) |> should equal "($_count + 1)"
        Expr.toString (Expr.multiply (Expr.read count) (Expr.int 2)) |> should equal "($_count * 2)"
        Expr.toString (Expr.divide (Expr.read count) (Expr.int 2)) |> should equal "($_count / 2)"
        Expr.toString (Expr.remainder (Expr.read count) (Expr.int 3)) |> should equal "($_count % 3)"

    [<Fact>]
    let ``Comparisons use the strict JavaScript operators`` () =
        Expr.toString (Expr.greater (Expr.read count) (Expr.int 5)) |> should equal "($_count > 5)"
        Expr.toString (Expr.less (Expr.read count) (Expr.int 5)) |> should equal "($_count < 5)"
        Expr.toString (Expr.atLeast (Expr.read count) (Expr.int 5)) |> should equal "($_count >= 5)"
        Expr.toString (Expr.atMost (Expr.read count) (Expr.int 5)) |> should equal "($_count <= 5)"
        Expr.toString (Expr.equal (Expr.read count) (Expr.int 5)) |> should equal "($_count === 5)"
        Expr.toString (Expr.notEqual (Expr.read count) (Expr.int 5)) |> should equal "($_count !== 5)"

    [<Fact>]
    let ``Boolean expressions combine`` () =
        let big = Expr.greater (Expr.read count) (Expr.int 5)
        Expr.toString (Expr.andAlso big (Expr.read menuOpen)) |> should equal "(($_count > 5) && $_menuOpen)"
        Expr.toString (Expr.orElse big (Expr.read menuOpen)) |> should equal "(($_count > 5) || $_menuOpen)"
        Expr.toString (Expr.negate (Expr.read menuOpen)) |> should equal "(!$_menuOpen)"

    [<Fact>]
    let ``A condition picks one of two values of the same type`` () =
        Expr.toString (Expr.ifElse (Expr.read menuOpen) (Expr.string "true") (Expr.string "false"))
        |> should equal "($_menuOpen ? 'true' : 'false')"

    [<Fact>]
    let ``Text is joined with plus, and empty text is an empty string`` () =
        Expr.toString (Expr.concat [ Expr.string "Hello "; Expr.read firstName; Expr.string "!" ])
        |> should equal "('Hello ' + $form.firstName + '!')"
        Expr.toString (Expr.concat []) |> should equal "''"

    [<Fact>]
    let ``Any value can be turned into text`` () =
        Expr.toString (Expr.toText (Expr.read count)) |> should equal "String($_count)"

    [<Fact>]
    let ``unsafeRaw passes JavaScript through, for what the typed functions do not cover`` () =
        Expr.toString (Expr.unsafeRaw<string> "evt.key") |> should equal "evt.key"

    // Statements

    [<Fact>]
    let ``A statement sets a signal to an expression of the same type`` () =
        Stmt.toString (Stmt.set count (Expr.add (Expr.read count) (Expr.int 1))) |> should equal "$_count = ($_count + 1)"
        Stmt.toString (Stmt.set isOn (Expr.bool true)) |> should equal "$$on = true"

    [<Fact>]
    let ``A statement can flip a boolean signal`` () =
        Stmt.toString (Stmt.toggle menuOpen) |> should equal "$_menuOpen = !$_menuOpen"

    [<Fact>]
    let ``Statements run one after another`` () =
        Stmt.toString (Stmt.all [ Stmt.toggle menuOpen; Stmt.set count (Expr.int 0) ])
        |> should equal "$_menuOpen = !$_menuOpen; $_count = 0"

    [<Fact>]
    let ``Backend actions are statements`` () =
        Stmt.toString (Stmt.get "/items") |> should equal "@get('/items')"
        Stmt.toString (Stmt.post "/items") |> should equal "@post('/items')"
        Stmt.toString (Stmt.put "/items") |> should equal "@put('/items')"
        Stmt.toString (Stmt.patch "/items") |> should equal "@patch('/items')"
        Stmt.toString (Stmt.delete "/items") |> should equal "@delete('/items')"
        Stmt.toString (Stmt.query "/items") |> should equal "@query('/items')"

    [<Fact>]
    let ``A URL cannot break out of its quotes`` () =
        Stmt.toString (Stmt.get "/x');alert(1);//") |> should equal """@get('/x\');alert(1);//')"""

    [<Fact>]
    let ``Backend actions take request options, and write the same as the string helpers`` () =
        let options = { RequestOptions.Defaults with Retry = OnError }
        Stmt.toString (Stmt.postWith "/items" options) |> should equal (Ds.post ("/items", options))

    // Attributes take typed values

    [<Fact>]
    let ``Ds.text, Ds.show and Ds.class' take expressions`` () =
        renderAttr (Ds.text (Expr.read count)) |> should equal """<div data-text="$_count"></div>"""
        renderAttr (Ds.show (Expr.read menuOpen)) |> should equal """<div data-show="$_menuOpen"></div>"""
        renderAttr (Ds.class' ("active", Expr.read menuOpen)) |> should equal """<div data-class:active="$_menuOpen"></div>"""

    [<Fact>]
    let ``Ds.attr' and Ds.style take expressions`` () =
        renderAttr (Ds.attr' ("aria-expanded", Expr.ifElse (Expr.read menuOpen) (Expr.string "true") (Expr.string "false")))
        |> should equal """<div data-attr:aria-expanded="($_menuOpen ? 'true' : 'false')"></div>"""
        renderAttr (Ds.style ("opacity", Expr.read count))
        |> should equal """<div data-style:opacity="$_count"></div>"""

    [<Fact>]
    let ``Ds.signal creates a signal with a value of its type`` () =
        renderAttr (Ds.signal (count, 0)) |> should equal """<div data-signals:_count="0"></div>"""
        renderAttr (Ds.signal (menuOpen, false)) |> should equal """<div data-signals:_menu-open="false"></div>"""
        renderAttr (Ds.signal (firstName, "Ada")) |> should equal """<div data-signals:form.first-name="'Ada'"></div>"""

    [<Fact>]
    let ``Ds.signal declares a Rocket component signal by its plain name, which Rocket scopes to the instance`` () =
        renderAttr (Ds.signal (isOn, false)) |> should equal """<div data-signals:on="false"></div>"""

    [<Fact>]
    let ``Ds.signal escapes a text value`` () =
        renderAttr (Ds.signal (firstName, "it's \"q\""))
        |> should equal """<div data-signals:form.first-name="'it\'s &quot;q&quot;'"></div>"""

    [<Fact>]
    let ``Ds.bind and Ds.indicator take signals`` () =
        renderAttr (Ds.bind firstName) |> should equal """<div data-bind:form.first-name></div>"""
        renderAttr (Ds.indicator (Signal.browser<bool> "loading")) |> should equal """<div data-indicator:_loading></div>"""

    [<Fact>]
    let ``Ds.computed takes a signal and an expression of its type`` () =
        let total = Signal.server<int> "total"
        renderAttr (Ds.computed (total, Expr.multiply (Expr.read count) (Expr.int 2)))
        |> should equal """<div data-computed:total="($_count * 2)"></div>"""

    [<Fact>]
    let ``Events take statements`` () =
        renderAttr (Ds.onClick (Stmt.toggle menuOpen)) |> should equal """<div data-on:click="$_menuOpen = !$_menuOpen"></div>"""
        renderAttr (Ds.onEvent ("keydown", Stmt.set count (Expr.int 0), [ Window ]))
        |> should equal """<div data-on:keydown__window="$_count = 0"></div>"""
        renderAttr (Ds.onInit (Stmt.get "/stream")) |> should equal """<div data-init="@get('/stream')"></div>"""
        renderAttr (Ds.effect (Stmt.set count (Expr.int 1))) |> should equal """<div data-effect="$_count = 1"></div>"""

    [<Fact>]
    let ``Timed events take statements`` () =
        renderAttr (Ds.onInterval (Stmt.get "/tick", 2000)) |> should equal """<div data-on-interval__duration.2000ms="@get('/tick')"></div>"""
        renderAttr (Ds.onIntersect (Stmt.get "/more", onlyOnce = true)) |> should equal """<div data-on-intersect__once="@get('/more')"></div>"""

    // Rocket

    [<Fact>]
    let ``Rocket.templateIf takes an expression`` () =
        renderNode (Rocket.templateIf (Expr.read isOn, [ Text.raw "on" ]))
        |> should equal """<template data-if="$$on">on</template>"""
        renderNode (Rocket.templateElseIf (Expr.negate (Expr.read isOn), [ Text.raw "off" ]))
        |> should equal """<template data-else-if="(!$$on)">off</template>"""

    [<Fact>]
    let ``Rocket.forEach gives the rows a typed item and index, so nothing is matched by name`` () =
        let items = Signal.rocket<string list> "items"
        renderNode (Rocket.forEach (Expr.read items, fun item index -> [ Elem.li [ Ds.text (Expr.concat [ Expr.toText index; Expr.string ": "; item ]) ] [] ]))
        |> should equal """<template data-for="$$items"><li data-text="(String(i) + ': ' + item)"></li></template>"""

    [<Fact>]
    let ``Rocket.forEach can name the item and the index`` () =
        let items = Signal.rocket<string list> "items"
        renderNode (Rocket.forEach (Expr.read items, (fun item _ -> [ Elem.li [ Ds.text item ] [] ]), itemName = "entry", indexName = "n"))
        |> should equal """<template data-for="entry, n in $$items"><li data-text="entry"></li></template>"""
