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

    // @setAll takes (value, filter) and @toggleAll takes (filter), in Datastar 1.0.4 and in RC.7

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
    let ``Ds.peek wraps the expression in a function for the peek action`` () =
        Ds.peek "$count"
        |> should equal "@peek(() => $count)"

    // data-bind on custom elements and web components

    [<Fact>]
    let ``Ds.bindProp binds a signal to an element property`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "checkedState", "checked"))
        |> should equal """<div data-bind:checked-state__prop.checked></div>"""

    [<Fact>]
    let ``Ds.bindProp writes the property name in kebab-case`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "val", "someProp"))
        |> should equal """<div data-bind:val__prop.some-prop></div>"""

    [<Fact>]
    let ``Ds.bindProp can also list events`` () =
        renderAttr (Ds.bindProp (SignalPath.sp "val", "value", [ "input"; "change" ]))
        |> should equal """<div data-bind:val__prop.value__event.input.change></div>"""

    [<Fact>]
    let ``Ds.bindEvent lists the events`` () =
        renderAttr (Ds.bindEvent (SignalPath.sp "val", "input", [ "change" ]))
        |> should equal """<div data-bind:val__event.input.change></div>"""

    // The __case modifier changes the casing of the name an attribute creates (bind, class, computed, indicator, on, ref, signals)

    [<Fact>]
    let ``Ds.withCase adds the case modifier to an attribute with a value`` () =
        renderAttr (Ds.signal (SignalPath.sp "myValue", 1) |> Ds.withCase CaseStyle.Snake)
        |> should equal """<div data-signals:my-value__case.snake="1"></div>"""

    [<Fact>]
    let ``Ds.withCase adds the case modifier to an attribute without a value`` () =
        renderAttr (Ds.bind (SignalPath.sp "myValue") |> Ds.withCase CaseStyle.Pascal)
        |> should equal """<div data-bind:my-value__case.pascal></div>"""

    [<Fact>]
    let ``Ds.withCase can name each of the four styles`` () =
        let styleOf caseStyle = renderAttr (Ds.indicator (SignalPath.sp "myValue") |> Ds.withCase caseStyle)
        styleOf CaseStyle.Camel |> should equal """<div data-indicator:my-value__case.camel></div>"""
        styleOf CaseStyle.Kebab |> should equal """<div data-indicator:my-value__case.kebab></div>"""
        styleOf CaseStyle.Snake |> should equal """<div data-indicator:my-value__case.snake></div>"""
        styleOf CaseStyle.Pascal |> should equal """<div data-indicator:my-value__case.pascal></div>"""

    [<Fact>]
    let ``Ds.withCase keeps modifiers that are already there`` () =
        renderAttr (Ds.signal (SignalPath.sp "myValue", 1, ifMissing = true) |> Ds.withCase CaseStyle.Snake)
        |> should equal """<div data-signals:my-value__ifmissing__case.snake="1"></div>"""

    [<Fact>]
    let ``Ds.withCase on an event name lets a camelCase event be listened to`` () =
        renderAttr (Ds.onEvent ("my-event", "$seen = true") |> Ds.withCase CaseStyle.Camel)
        |> should equal """<div data-on:my-event__case.camel="$seen = true"></div>"""

    // Content Security Policy: Datastar reads data-nonce from the <html> element (csp.ts)

    [<Fact>]
    let ``Ds.nonce writes data-nonce`` () =
        renderAttr (Ds.nonce "r4nd0m")
        |> should equal """<div data-nonce="r4nd0m"></div>"""

    [<Fact>]
    let ``Ds.nonce escapes the value so it cannot break out of the attribute`` () =
        renderAttr (Ds.nonce "a\"b")
        |> should equal """<div data-nonce="a&quot;b"></div>"""

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
    let ``RequestOptions CustomJson is sent as the payload object`` () =
        Ds.post ("/x", { RequestOptions.Defaults with ContentType = CustomJson {| a = 1 |} })
        |> should equal """@post('/x',{&quot;contentType&quot;:&quot;json&quot;,&quot;payload&quot;:{&quot;a&quot;:1}})"""

    // Retry options: the names are the ones Datastar 1.0.4 reads (createHttpMethod in fetch.ts).
    // RC.8 and earlier read retryMaxWaitMs; since 1.0.0 the name is retryMaxWait.

    [<Fact>]
    let ``RequestOptions RetryMaxWait is sent as retryMaxWait`` () =
        Ds.get ("/x", { RequestOptions.Defaults with RetryMaxWait = System.TimeSpan.FromSeconds 5.0 })
        |> should equal """@get('/x',{&quot;retryMaxWait&quot;:5000})"""

    [<Fact>]
    let ``RequestOptions Retry is sent when it is not the default`` () =
        Ds.get ("/x", { RequestOptions.Defaults with Retry = OnError })
        |> should equal """@get('/x',{&quot;retry&quot;:&quot;error&quot;})"""
        Ds.get ("/x", { RequestOptions.Defaults with Retry = OnAlways })
        |> should equal """@get('/x',{&quot;retry&quot;:&quot;always&quot;})"""
        Ds.get ("/x", { RequestOptions.Defaults with Retry = OnNever })
        |> should equal """@get('/x',{&quot;retry&quot;:&quot;never&quot;})"""

    [<Fact>]
    let ``RequestOptions retry interval, scaler and count keep their names`` () =
        Ds.get ("/x", { RequestOptions.Defaults with RetryInterval = System.TimeSpan.FromMilliseconds 250.0 })
        |> should equal """@get('/x',{&quot;retryInterval&quot;:250})"""
        Ds.get ("/x", { RequestOptions.Defaults with RetryScaler = 3.0 })
        |> should equal """@get('/x',{&quot;retryScaler&quot;:3})"""
        Ds.get ("/x", { RequestOptions.Defaults with RetryMaxCount = 4 })
        |> should equal """@get('/x',{&quot;retryMaxCount&quot;:4})"""

    [<Fact>]
    let ``RequestOptions Defaults write no options, so Datastar's defaults apply`` () =
        Ds.get ("/x", RequestOptions.Defaults)
        |> should equal "@get('/x',{})"

    [<Fact>]
    let ``RequestOptions.Defaults is one shared object, because it is read many times for every option that is written`` () =
        obj.ReferenceEquals(RequestOptions.Defaults, RequestOptions.Defaults) |> should equal true

    [<Fact>]
    let ``RequestOptions OpenWhenHidden is not set by default`` () =
        RequestOptions.Defaults.OpenWhenHidden
        |> should equal (ValueNone : bool voption)

    [<Fact>]
    let ``RequestOptions OpenWhenHidden can be set to false, even for a POST`` () =
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

