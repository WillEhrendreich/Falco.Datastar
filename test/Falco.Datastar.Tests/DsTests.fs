namespace Falco.Datastar.Tests

open Falco.Datastar
open Falco.Markup
open FsUnit.Xunit
open Xunit

[<AutoOpen>]
module private Common =
    let renderAttr attr =
        Elem.div [ attr ] [ ]
        |> renderNode

module DsTests =
    [<Fact>]
    let ``Ds.bind should create an attribute`` () =
        renderAttr (Ds.bind "signalPath")
        |> should equal """<div data-bind:signal-path></div>"""

    [<Fact>]
    let ``Ds.post`` () =
        Ds.post "/channel"
        |> should equal """@post('/channel')"""

    [<Fact>]
    let ``Ds.post with Form`` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = Form })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;})"""

    [<Fact>]
    let ``Ds.post with SelectedForm`` () =
        Ds.post ("/channel", { RequestOptions.Defaults with ContentType = (SelectedForm "myForm") })
        |> should equal """@post('/channel',{&quot;contentType&quot;:&quot;form&quot;,&quot;selector&quot;:&quot;myForm&quot;})"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Exclude`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueNone; ExcludePattern = ValueSome "files" }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ exclude: /files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Include`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files"; ExcludePattern = ValueNone }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ include: /files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Both`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files$"; ExcludePattern = ValueSome "^files" }
        renderAttr (Ds.jsonSignalsOptions filterFiles)
        |> should equal """<div data-json-signals="{ include: /files$/,exclude: /^files/ }"></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Terse`` () =
        renderAttr (Ds.jsonSignalsOptions (terse = true))
        |> should equal """<div data-json-signals__terse></div>"""

    [<Fact>]
    let ``Ds.jsonSignalsOptions Terse and Both Filters`` () =
        let filterFiles : SignalsFilter = { IncludePattern = ValueSome "files$"; ExcludePattern = ValueSome "^files" }
        renderAttr (Ds.jsonSignalsOptions (filterFiles, terse = true))
        |> should equal """<div data-json-signals__terse="{ include: /files$/,exclude: /^files/ }"></div>"""

    [<Fact>]
    let ``Ds.onIntersect No Options`` () =
        renderAttr (Ds.onIntersect "@get('/hello')")
        |> should equal """<div data-on-intersect="@get('/hello')"></div>"""

    [<Fact>]
    let ``Ds.onIntersect Threshold`` () =
        renderAttr (Ds.onIntersect ("@get('/hello')", threshold=50))
        |> should equal """<div data-on-intersect__threshold.50="@get('/hello')"></div>"""

    [<Fact>]
    let ``Ds.style`` () =
        renderAttr (Ds.style ("display", "$hiding && 'none'"))
        |> should equal """<div data-style:display="$hiding && 'none'"></div>"""

    // Datastar baseline

    [<Fact>]
    let ``Ds.cdnSrc is pinned to the Datastar 1.0.4 bundle`` () =
        Ds.cdnSrc
        |> should equal "https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar.js"

    [<Fact>]
    let ``Ds.rocketCdnSrc is the Rocket bundle of the same version`` () =
        Ds.rocketCdnSrc
        |> should equal "https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar-rocket.js"

    [<Fact>]
    let ``Ds.cdnScript and Ds.rocketCdnScript render module script tags`` () =
        renderNode Ds.cdnScript
        |> should equal """<script type="module" src="https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar.js"></script>"""
        renderNode Ds.rocketCdnScript
        |> should equal """<script type="module" src="https://cdn.jsdelivr.net/gh/starfederation/datastar@v1.0.4/bundles/datastar-rocket.js"></script>"""

    // @setAll / @toggleAll take (value, filter) and (filter) since 1.0.0

    [<Fact>]
    let ``Ds.setAll passes the value first and the prefix as an include filter`` () =
        Ds.setAll ("foo.", true)
        |> should equal """@setAll(true, { include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.setAll emits numbers unquoted and strings quoted`` () =
        Ds.setAll ("foo.", 5) |> should equal """@setAll(5, { include: /^foo\./ })"""
        Ds.setAll ("foo.", "x") |> should equal """@setAll('x', { include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.setAll escapes string values so they cannot break out of the attribute`` () =
        Ds.setAll ("foo.", "it's \"q\"")
        |> should equal """@setAll('it\'s &quot;q&quot;', { include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.setAllFiltered without a filter sets every signal`` () =
        Ds.setAllFiltered (false, SignalsFilter.None)
        |> should equal "@setAll(false)"

    [<Fact>]
    let ``Ds.setAllFiltered uses the given include and exclude filter`` () =
        Ds.setAllFiltered (true, { IncludePattern = ValueSome "^form\\."; ExcludePattern = ValueSome "\\.id$" })
        |> should equal """@setAll(true, { include: /^form\./,exclude: /\.id$/ })"""

    [<Fact>]
    let ``Ds.toggleAll takes the prefix as an include filter`` () =
        Ds.toggleAll "foo."
        |> should equal """@toggleAll({ include: /^foo\./ })"""

    [<Fact>]
    let ``Ds.toggleAllFiltered without a filter toggles every signal`` () =
        Ds.toggleAllFiltered SignalsFilter.None
        |> should equal "@toggleAll()"

    [<Fact>]
    let ``Ds.peek wraps the expression so it reads signals without subscribing`` () =
        Ds.peek "$count"
        |> should equal "@peek(() => $count)"

    // data-bind on custom elements and web components

    [<Fact>]
    let ``Ds.bindProp binds a signal to an element property`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "checkedState", "checked"))
        |> should equal """<div data-bind:checked-state__prop.checked></div>"""

    [<Fact>]
    let ``Ds.bindProp kebab-cases the property because the HTML parser lowercases attribute names`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "val", "someProp"))
        |> should equal """<div data-bind:val__prop.some-prop></div>"""

    [<Fact>]
    let ``Ds.bindProp can also name the events that sync the signal`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "val", "value", [ "input"; "change" ]))
        |> should equal """<div data-bind:val__prop.value__event.input.change></div>"""

    [<Fact>]
    let ``Ds.bindEvent overrides the events that sync the signal`` () =
        renderAttr (Ds.bindEvent (SignalPath.sp "val", [ "input"; "change" ]))
        |> should equal """<div data-bind:val__event.input.change></div>"""

    // data-on target and fetch options

    [<Fact>]
    let ``Ds.onEvent Document listens on the document`` () =
        renderAttr (Ds.onEvent ("keydown", "$k = evt.key", [ Document ]))
        |> should equal """<div data-on:keydown__document="$k = evt.key"></div>"""

    [<Fact>]
    let ``RequestOptions Cleanup cancels the request when the element is removed`` () =
        Ds.get ("/x", { RequestOptions.Defaults with RequestCancellation = Cleanup })
        |> should equal """@get('/x',{&quot;requestCancellation&quot;:&quot;cleanup&quot;})"""

    [<Fact>]
    let ``RequestOptions CustomJson is sent as the payload object, not the removed override string`` () =
        Ds.post ("/x", { RequestOptions.Defaults with ContentType = CustomJson {| a = 1 |} })
        |> should equal """@post('/x',{&quot;contentType&quot;:&quot;json&quot;,&quot;payload&quot;:{&quot;a&quot;:1}})"""

    [<Fact>]
    let ``RequestOptions Defaults send nothing so that Datastar's own defaults apply`` () =
        Ds.get ("/x", RequestOptions.Defaults)
        |> should equal "@get('/x',{})"

    [<Fact>]
    let ``RequestOptions OpenWhenHidden is unset by default because Datastar defaults it per method`` () =
        RequestOptions.Defaults.OpenWhenHidden
        |> should equal (ValueNone : bool voption)

    [<Fact>]
    let ``RequestOptions OpenWhenHidden false can be sent, which a POST needs because Datastar defaults it to true`` () =
        Ds.post ("/x", { RequestOptions.Defaults with OpenWhenHidden = ValueSome false })
        |> should equal """@post('/x',{&quot;openWhenHidden&quot;:false})"""

    [<Fact>]
    let ``RequestOptions OpenWhenHidden true is sent as a JSON boolean`` () =
        Ds.get ("/x", { RequestOptions.Defaults with OpenWhenHidden = ValueSome true })
        |> should equal """@get('/x',{&quot;openWhenHidden&quot;:true})"""

    [<Fact>]
    let ``Ds.query creates a query action`` () =
        Ds.query "/search" |> should equal """@query('/search')"""

    [<Fact>]
    let ``Ds.query takes request options`` () =
        Ds.query ("/search", { RequestOptions.Defaults with RequestCancellation = Disabled })
        |> should equal """@query('/search',{&quot;requestCancellation&quot;:&quot;disabled&quot;})"""

    [<Fact>]
    let ``RequestOptions ResponseOverrides for elements only lists the values that were set`` () =
        let overrides = OverrideElements { ElementsOverrides.None with Selector = ValueSome "#target"; Mode = ValueSome Append }
        Ds.get ("/x", { RequestOptions.Defaults with ResponseOverrides = ValueSome overrides })
        |> should equal """@get('/x',{&quot;responseOverrides&quot;:{&quot;selector&quot;:&quot;#target&quot;,&quot;mode&quot;:&quot;append&quot;}})"""

    [<Fact>]
    let ``RequestOptions ResponseOverrides can turn on view transitions`` () =
        let overrides = OverrideElements { ElementsOverrides.None with UseViewTransition = ValueSome true }
        Ds.get ("/x", { RequestOptions.Defaults with ResponseOverrides = ValueSome overrides })
        |> should equal """@get('/x',{&quot;responseOverrides&quot;:{&quot;useViewTransition&quot;:true}})"""

    [<Fact>]
    let ``RequestOptions ResponseOverrides for signals sets onlyIfMissing`` () =
        Ds.get ("/x", { RequestOptions.Defaults with ResponseOverrides = ValueSome (OverrideSignals true) })
        |> should equal """@get('/x',{&quot;responseOverrides&quot;:{&quot;onlyIfMissing&quot;:true}})"""

