namespace Falco.Datastar.Tests

open System.IO
open System.Text
open Falco.Datastar
open FsUnit.Xunit
open Microsoft.AspNetCore.Http
open Xunit

type TestSignals = { A: int }

// Datastar 1.0.4 sends the signals of a @get and a @delete in the `datastar` query parameter,
// and the signals of the other actions in the request body (methodSupportsRequestBody in fetch.ts).
// The SDK only reads a @delete correctly from version 1.3.0.
module RequestTests =
    let private queryWithSignals = "?datastar=%7B%22a%22%3A1%7D"

    let private contextFor (verb: string) (query: string) (body: string) =
        let ctx = DefaultHttpContext()
        ctx.Request.Method <- verb
        ctx.Request.QueryString <- QueryString query
        ctx.Request.ContentType <- "application/json"
        ctx.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes body)
        ctx

    let private readJson verb query body =
        use document = (Request.getSignalsJson (contextFor verb query body)).GetAwaiter().GetResult()
        document.RootElement.GetProperty("a").GetInt32()

    let private readTyped verb query body =
        (Request.getSignals<TestSignals> (contextFor verb query body)).GetAwaiter().GetResult()

    [<Fact>]
    let ``Request.getSignalsJson reads a GET from the query string`` () =
        readJson "GET" queryWithSignals "" |> should equal 1

    [<Fact>]
    let ``Request.getSignalsJson reads a POST from the body`` () =
        readJson "POST" "" """{"a":1}""" |> should equal 1

    [<Fact>]
    let ``Request.getSignalsJson reads a DELETE from the query string`` () =
        readJson "DELETE" queryWithSignals "" |> should equal 1

    [<Fact>]
    let ``Request.getSignals reads a DELETE from the query string`` () =
        readTyped "DELETE" queryWithSignals ""
        |> should equal (ValueSome { A = 1 })

    [<Fact>]
    let ``Request.getSignals reads a POST from the body`` () =
        readTyped "POST" "" """{"a":1}"""
        |> should equal (ValueSome { A = 1 })
