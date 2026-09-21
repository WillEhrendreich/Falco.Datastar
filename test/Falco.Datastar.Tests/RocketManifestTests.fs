namespace Falco.Datastar.Tests

open System
open System.IO
open System.Text
open System.Text.Json
open Falco.Datastar
open FsUnit.Xunit
open Microsoft.AspNetCore.Http
open Xunit

module RocketManifestTests =
    // The body that Datastar 1.0.4's publishRocketManifests posted from a real browser, for two components
    let private posted = """{"version":1,"generatedAt":"2026-09-21T17:42:04.838Z","components":[{"tag":"demo-card","props":[{"name":"title","attribute":"title","type":"string","default":"Card","required":false,"docs":{"description":"The card title.","label":"Title","control":"text","placeholder":"Type a title"}},{"name":"maxCount","attribute":"max-count","type":"number","default":3,"required":false},{"name":"open","attribute":"open","type":"boolean","default":false,"required":false},{"name":"due","attribute":"due","type":"date","default":"2026-09-21T17:42:04.539Z","required":false},{"name":"settings","attribute":"settings","type":"json","default":{"a":1},"required":false},{"name":"payload","attribute":"payload","type":"binary","default":{},"required":false},{"name":"tags","attribute":"tags","type":"array","default":[],"required":false},{"name":"profile","attribute":"profile","type":"object","default":{"name":"Anonymous","age":0},"required":false},{"name":"theme","attribute":"theme","type":"oneOf","default":"system","required":false,"values":["light","dark","system"]}],"slots":[{"name":"default","description":"Body."},{"name":"footer"}],"events":[{"kind":"custom-event","name":"close","bubbles":true,"composed":true,"description":"Dismiss."},{"kind":"event","name":"ready"}]},{"tag":"demo-plain","props":[],"slots":[],"events":[]}]}"""

    let private parsed () =
        match RocketManifest.parse posted with
        | Ok document -> document
        | Error message -> failwith message

    let private component' tag =
        (parsed ()).Components |> List.find (fun c -> c.Tag = tag)

    let private prop tag name =
        (component' tag).Props |> List.find (fun p -> p.Name = name)

    [<Fact>]
    let ``RocketManifest.parse reads the version, the time and the components in order`` () =
        let document = parsed ()
        document.Version |> should equal 1
        document.GeneratedAt |> should equal (DateTimeOffset.Parse("2026-09-21T17:42:04.838Z"))
        document.Components |> List.map (fun c -> c.Tag) |> should equal [ "demo-card"; "demo-plain" ]

    [<Fact>]
    let ``RocketManifest.parse reads a component without props, slots or events`` () =
        let plain = component' "demo-plain"
        plain.Props |> should be Empty
        plain.Slots |> should be Empty
        plain.Events |> should be Empty

    [<Fact>]
    let ``RocketManifest.parse gives each prop the attribute name Rocket derived`` () =
        (component' "demo-card").Props
        |> List.map (fun p -> p.Name, p.Attribute)
        |> should equal [ "title", "title"; "maxCount", "max-count"; "open", "open"; "due", "due"; "settings", "settings"
                          "payload", "payload"; "tags", "tags"; "profile", "profile"; "theme", "theme" ]

    [<Fact>]
    let ``RocketManifest.parse maps the codec name of each prop to a case`` () =
        (component' "demo-card").Props
        |> List.map (fun p -> p.Type)
        |> should equal [ RocketPropType.String; RocketPropType.Number; RocketPropType.Boolean; RocketPropType.Date; RocketPropType.Json
                          RocketPropType.Binary; RocketPropType.Array; RocketPropType.Object; RocketPropType.OneOf ]

    [<Fact>]
    let ``RocketManifest.parse keeps a codec name it does not know instead of failing`` () =
        let json = """{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z","components":[{"tag":"x-y","props":[{"name":"p","attribute":"p","type":"hologram","default":null,"required":false}],"slots":[],"events":[]}]}"""
        match RocketManifest.parse json with
        | Ok document -> document.Components.Head.Props.Head.Type |> should equal (RocketPropType.Other "hologram")
        | Error message -> failwith message

    [<Fact>]
    let ``RocketManifest.parse keeps the default of a prop as JSON`` () =
        (prop "demo-card" "title").Default.GetString() |> should equal "Card"
        (prop "demo-card" "maxCount").Default.GetInt32() |> should equal 3
        (prop "demo-card" "open").Default.GetBoolean() |> should equal false
        (prop "demo-card" "settings").Default.GetProperty("a").GetInt32() |> should equal 1
        (prop "demo-card" "profile").Default.GetProperty("name").GetString() |> should equal "Anonymous"

    [<Fact>]
    let ``RocketManifest.parse reads the allowed values of a oneOf prop`` () =
        (prop "demo-card" "theme").Values
        |> ValueOption.map (List.map (fun v -> v.GetString()))
        |> should equal (ValueSome [ "light"; "dark"; "system" ])
        (prop "demo-card" "title").Values |> should equal (ValueNone : JsonElement list voption)

    [<Fact>]
    let ``RocketManifest.parse reads the documentation of a prop when the component gave it`` () =
        let docs = (prop "demo-card" "title").Docs
        docs |> ValueOption.map (fun d -> d.Label) |> should equal (ValueSome (ValueSome "Title"))
        docs |> ValueOption.map (fun d -> d.Description) |> should equal (ValueSome (ValueSome "The card title."))
        docs |> ValueOption.map (fun d -> d.Control) |> should equal (ValueSome (ValueSome "text"))
        docs |> ValueOption.map (fun d -> d.Placeholder) |> should equal (ValueSome (ValueSome "Type a title"))
        (prop "demo-card" "maxCount").Docs |> should equal (ValueNone : RocketPropDocs voption)

    [<Fact>]
    let ``RocketManifest.parse reads slots with and without a description`` () =
        (component' "demo-card").Slots
        |> List.map (fun s -> s.Name, s.Description)
        |> should equal [ "default", ValueSome "Body."; "footer", ValueNone ]

    [<Fact>]
    let ``RocketManifest.parse reads events, including the ones with no description`` () =
        let events = (component' "demo-card").Events
        events |> List.map (fun e -> e.Name) |> should equal [ "close"; "ready" ]
        events |> List.map (fun e -> e.Kind) |> should equal [ RocketEventKind.CustomEvent; RocketEventKind.Event ]
        events |> List.map (fun e -> e.Bubbles) |> should equal [ ValueSome true; ValueNone ]
        events |> List.map (fun e -> e.Composed) |> should equal [ ValueSome true; ValueNone ]
        events |> List.map (fun e -> e.Description) |> should equal [ ValueSome "Dismiss."; ValueNone ]

    [<Fact>]
    let ``RocketManifest.parse says which version it reads, and what to do, when it gets another one`` () =
        match RocketManifest.parse """{"version":2,"generatedAt":"2026-01-01T00:00:00.000Z","components":[]}""" with
        | Error message ->
            message |> should haveSubstring "reads Rocket manifest version 1, but the document is version 2"
            message |> should haveSubstring "Update Falco.Datastar"
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``RocketManifest.parse returns an error for text that is not JSON`` () =
        match RocketManifest.parse "not json" with
        | Error message -> message |> should startWith "The manifest is not valid JSON"
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``RocketManifest.parse names the property that is missing`` () =
        match RocketManifest.parse """{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z"}""" with
        | Error message -> message |> should haveSubstring "components"
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``Request.getRocketManifests reads the body of a POST`` () =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- "POST"
        ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes posted)
        match (Request.getRocketManifests ctx).GetAwaiter().GetResult() with
        | Ok document -> document.Components |> List.length |> should equal 2
        | Error message -> failwith message

    let private wrap (component':string) =
        $"""{{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z","components":[{component'}]}}"""

    [<Fact>]
    let ``RocketManifest.parse returns an error, and does not throw, when components is not a list`` () =
        match RocketManifest.parse """{"version":1,"generatedAt":"2026-01-01T00:00:00.000Z","components":5}""" with
        | Error message -> message |> should haveSubstring "components"
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``RocketManifest.parse names the prop and the component when a prop property is missing`` () =
        match RocketManifest.parse (wrap """{"tag":"my-card","props":[{"name":"count","type":"number","default":0}]}""") with
        | Error message ->
            message |> should haveSubstring "attribute"
            message |> should haveSubstring "count"
            message |> should haveSubstring "my-card"
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``RocketManifest.parse gives the position of a prop that has no name`` () =
        match RocketManifest.parse (wrap """{"tag":"my-card","props":[{"name":"a","attribute":"a","type":"string","default":""},{"attribute":"b","type":"string","default":""}]}""") with
        | Error message ->
            message |> should haveSubstring "prop 2"
            message |> should haveSubstring "my-card"
        | Ok _ -> failwith "expected an error"

    [<Fact>]
    let ``Request.getRocketManifests refuses a body that is larger than one mebibyte`` () =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- "POST"
        ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes (String('x', 1024 * 1024 + 1)))
        match (Request.getRocketManifests ctx).GetAwaiter().GetResult() with
        | Error message -> message |> should haveSubstring "larger than"
        | Ok _ -> failwith "expected an error"
