namespace Falco.Datastar.Tests

open System
open System.Globalization
open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

module RocketTests =
    let private renderAttr attr =
        Elem.create "my-el" [ attr ] []
        |> renderNode

    // Expressions

    [<Fact>]
    let ``Rocket.local writes a $$ signal name`` () =
        Rocket.local "count" |> should equal "$$count"

    [<Fact>]
    let ``Rocket.call invokes a component action, with or without arguments`` () =
        Rocket.call "flip" |> should equal "@flip()"
        Rocket.call ("add", [ "1"; "'a'" ]) |> should equal "@add(1, 'a')"

    [<Fact>]
    let ``Rocket.root adds the __root modifier to a bind, computed or indicator`` () =
        renderAttr (Rocket.root (Ds.bind "query"))
        |> should equal """<my-el data-bind:query__root></my-el>"""
        renderAttr (Rocket.root (Ds.computed (SignalPath.sp "total", "$a + $b")))
        |> should equal """<my-el data-computed:total__root="$a + $b"></my-el>"""

    // Rocket builds each prop's attribute name with Datastar's kebab function (library/src/utils/text.ts).
    // The expected values below came from running that function, not from this library.

    [<Theory>]
    [<InlineData("count", "count")>]
    [<InlineData("maxCount", "max-count")>]
    [<InlineData("innerHTML", "inner-html")>]
    [<InlineData("HTMLThing", "html-thing")>]
    [<InlineData("parseURL", "parse-url")>]
    [<InlineData("userID", "user-id")>]
    [<InlineData("ID", "id")>]
    [<InlineData("x2", "x-2")>]
    [<InlineData("pos3d", "pos-3-d")>]
    [<InlineData("a1b2", "a-1-b-2")>]
    [<InlineData("value2Max", "value-2-max")>]
    [<InlineData("is_open", "is-open")>]
    [<InlineData("some prop", "some-prop")>]
    [<InlineData("XMLHttpRequest", "xml-http-request")>]
    [<InlineData("p95Latency", "p-95-latency")>]
    [<InlineData("h1Title", "h-1-title")>]
    let ``Rocket props get the attribute name Rocket expects`` (propName: string, attribute: string) =
        renderAttr (Rocket.propString (propName, "v"))
        |> should equal $"""<my-el {attribute}="v"></my-el>"""

    // Prop values: each helper writes what the matching codec reads (library/src/rocket/codecs.ts)

    [<Fact>]
    let ``Rocket.propString escapes the value so it cannot break out of the attribute`` () =
        renderAttr (Rocket.propString ("label", "say \"hi\" & <go>"))
        |> should equal """<my-el label="say &quot;hi&quot; &amp; &lt;go&gt;"></my-el>"""

    [<Fact>]
    let ``Rocket.propNumber accepts any numeric type`` () =
        renderAttr (Rocket.propNumber ("count", 5)) |> should equal """<my-el count="5"></my-el>"""
        renderAttr (Rocket.propNumber ("ratio", 1.5)) |> should equal """<my-el ratio="1.5"></my-el>"""
        renderAttr (Rocket.propNumber ("delta", -2L)) |> should equal """<my-el delta="-2"></my-el>"""
        renderAttr (Rocket.propNumber ("price", 9.99m)) |> should equal """<my-el price="9.99"></my-el>"""

    [<Fact>]
    let ``Rocket.propNumber ignores the current culture`` () =
        // Built from the invariant culture so the test does not need ICU, which slim CI images lack
        let commaDecimal = CultureInfo.InvariantCulture.Clone() :?> CultureInfo
        commaDecimal.NumberFormat.NumberDecimalSeparator <- ","
        let original = CultureInfo.CurrentCulture
        try
            CultureInfo.CurrentCulture <- commaDecimal
            renderAttr (Rocket.propNumber ("ratio", 1.5))
            |> should equal """<my-el ratio="1.5"></my-el>"""
        finally
            CultureInfo.CurrentCulture <- original

    [<Fact>]
    let ``Rocket.propBool writes false as well as true`` () =
        renderAttr (Rocket.propBool ("open", true)) |> should equal """<my-el open="true"></my-el>"""
        renderAttr (Rocket.propBool ("open", false)) |> should equal """<my-el open="false"></my-el>"""

    [<Fact>]
    let ``Rocket.propDate writes UTC ISO 8601 with milliseconds, as Date.toISOString does`` () =
        let value = DateTimeOffset(2026, 9, 21, 10, 30, 5, 120, TimeSpan.FromHours 2.0)
        renderAttr (Rocket.propDate ("due", value))
        |> should equal """<my-el due="2026-09-21T08:30:05.120Z"></my-el>"""

    [<Fact>]
    let ``Rocket.propJson writes camelCase JSON and escapes it for the attribute`` () =
        // anonymous records order their fields alphabetically
        renderAttr (Rocket.propJson ("point", {| X = 1; UserName = "a" |}))
        |> should equal """<my-el point="{&quot;userName&quot;:&quot;a&quot;,&quot;x&quot;:1}"></my-el>"""

    [<Fact>]
    let ``Rocket.propJson writes arrays as JSON`` () =
        renderAttr (Rocket.propJson ("items", [ 1; 2; 3 ]))
        |> should equal """<my-el items="[1,2,3]"></my-el>"""

    [<Fact>]
    let ``Rocket.propBin writes base64, which the browser decodes with atob`` () =
        renderAttr (Rocket.propBin ("data", [| 1uy; 2uy; 3uy |]))
        |> should equal """<my-el data="AQID"></my-el>"""

    // Template directives: Rocket always uses the attribute names data-for, data-if, data-else-if and data-else

    [<Fact>]
    let ``Rocket.templateFor writes no item or index names unless you pass them`` () =
        renderNode (Rocket.templateFor ("$$items", [ Text.raw "x" ]))
        |> should equal """<template data-for="$$items">x</template>"""

    [<Fact>]
    let ``Rocket.templateFor can name the item, and the index`` () =
        renderNode (Rocket.templateFor ("$$todos", [ Text.raw "x" ], item = "todo"))
        |> should equal """<template data-for="todo in $$todos">x</template>"""
        renderNode (Rocket.templateFor ("$$todos", [ Text.raw "x" ], item = "todo", index = "n"))
        |> should equal """<template data-for="todo, n in $$todos">x</template>"""

    [<Fact>]
    let ``Rocket.templateFor with only an index names the item "item"`` () =
        renderNode (Rocket.templateFor ("$$todos", [ Text.raw "x" ], index = "n"))
        |> should equal """<template data-for="item, n in $$todos">x</template>"""

    [<Fact>]
    let ``Rocket.templateIf, templateElseIf and templateElse make a chain`` () =
        renderNode (Rocket.templateIf ("$$n > 9", [ Text.raw "big" ]))
        |> should equal """<template data-if="$$n &gt; 9">big</template>"""
        renderNode (Rocket.templateElseIf ("$$n > 4", [ Text.raw "mid" ]))
        |> should equal """<template data-else-if="$$n &gt; 4">mid</template>"""
        renderNode (Rocket.templateElse [ Text.raw "small" ])
        |> should equal """<template data-else>small</template>"""

    [<Fact>]
    let ``Rocket.templateFor escapes the expression`` () =
        renderNode (Rocket.templateFor ("$$a.filter(x => x.n < 2 && x.s == \"q\")", []))
        |> should equal """<template data-for="$$a.filter(x =&gt; x.n &lt; 2 &amp;&amp; x.s == &quot;q&quot;)"></template>"""
