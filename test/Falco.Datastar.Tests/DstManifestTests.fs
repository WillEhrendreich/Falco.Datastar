namespace Falco.Datastar.Tests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open Falco.Datastar
open FsUnit.Xunit
open Microsoft.AspNetCore.Http
open Xunit

// Deterministic simulation of the Rocket manifest reader: what comes in over a connection that misbehaves, and what a page could post.
// The seed of a failing case is in the message. See Dst.fs for how to run more seeds and how to replay one.
module DstManifestTests =
    let private limit = 1024 * 1024
    let private buffer = 8192

    // A random manifest, made the way Rocket's publishRocketManifests makes one

    let private codecs = [| "string"; "number"; "boolean"; "date"; "json"; "js"; "binary"; "array"; "tuple"; "object"; "oneOf"; "custom"; "hologram" |]

    let private randomValue (random:Random) : JsonNode =
        match random.Next 6 with
        | 0 -> JsonValue.Create "text"
        | 1 -> JsonValue.Create (random.Next 1000)
        | 2 -> JsonValue.Create (random.Next 2 = 0)
        | 3 -> JsonArray(JsonValue.Create 1, JsonValue.Create "b")
        | 4 -> JsonObject([ KeyValuePair("a", JsonValue.Create 1 :> JsonNode) ])
        | _ -> null

    let private randomProp (random:Random) (index:int) : JsonNode =
        let prop = JsonObject()
        prop["name"] <- JsonValue.Create $"prop{index}"
        prop["attribute"] <- JsonValue.Create $"prop-{index}"
        prop["type"] <- JsonValue.Create (Dst.pick random codecs)
        // A codec that has no default leaves the key out
        if random.Next 5 > 0 then prop["default"] <- randomValue random
        prop["required"] <- JsonValue.Create (random.Next 2 = 0)
        if random.Next 3 = 0 then prop["values"] <- JsonArray(JsonValue.Create "a", JsonValue.Create "b")
        if random.Next 3 = 0 then prop["docs"] <- JsonObject([ KeyValuePair("description", JsonValue.Create "d" :> JsonNode) ])
        prop :> JsonNode

    let private randomComponent (random:Random) (index:int) : JsonNode =
        let component' = JsonObject()
        component'["tag"] <- JsonValue.Create $"my-component-{index}"
        component'["props"] <- JsonArray(Array.init (random.Next 6) (randomProp random))
        component'["slots"] <- JsonArray(Array.init (random.Next 3) (fun i -> JsonObject([ KeyValuePair("name", JsonValue.Create $"slot{i}" :> JsonNode) ]) :> JsonNode))
        component'["events"] <- JsonArray(Array.init (random.Next 3) (fun i ->
            let event = JsonObject()
            event["name"] <- JsonValue.Create $"event{i}"
            event["kind"] <- JsonValue.Create (Dst.pick random [| "event"; "custom-event"; "other" |])
            if random.Next 2 = 0 then event["bubbles"] <- JsonValue.Create true
            event :> JsonNode))
        component' :> JsonNode

    let private randomManifest (random:Random) : JsonObject =
        let manifest = JsonObject()
        manifest["version"] <- JsonValue.Create 1
        manifest["generatedAt"] <- JsonValue.Create "2026-09-21T17:42:04.838Z"
        manifest["components"] <- JsonArray(Array.init (random.Next 12) (randomComponent random))
        manifest

    /// The JSON of an element without the white space it was written with
    let private canonical (element:Text.Json.JsonElement) =
        match JsonNode.Parse(element.GetRawText()) with
        | null -> "null"
        | node -> node.ToJsonString()

    /// What a result stands for, as text, so that two results can be compared. JsonElement compares by reference.
    let private describe (result:Result<RocketManifestDocument, RocketManifestError>) =
        match result with
        | Error error -> $"Error {error.Message}"
        | Ok document ->
            let components =
                document.Components
                |> List.map (fun c ->
                    let props = c.Props |> List.map (fun p -> $"{p.Name}/{p.Attribute}/{p.Type}/{canonical p.Default}/{p.Required}/{p.Values.IsSome}/{p.Docs.IsSome}")
                    let slots = c.Slots |> List.map (fun s -> s.Name)
                    let events = c.Events |> List.map (fun e -> $"{e.Name}/{e.Kind}/{e.Bubbles}")
                    $"{c.Tag} [{String.Join(',', props)}] [{String.Join(',', slots)}] [{String.Join(',', events)}]")
            $"Ok v{document.Version} {document.GeneratedAt:o} {String.Join(';', components)}"

    // Mutations: change what a page could post into something that is not a manifest, in the way a bug or an attacker would

    /// Every node with the way to replace or remove it
    let rec private nodes (parent:JsonNode) : (JsonNode * string * int) list =
        match parent with
        | :? JsonObject as object ->
            [ for KeyValue (key, child) in Seq.toList object do
                yield (parent, key, -1)
                if not (isNull child) then yield! nodes child ]
        | :? JsonArray as array ->
            [ for index in 0 .. array.Count - 1 do
                yield (parent, "", index)
                if not (isNull array.[index]) then yield! nodes array.[index] ]
        | _ -> []

    let private mutate (random:Random) (root:JsonObject) =
        for _ in 1 .. random.Next(1, 4) do
            match nodes root with
            | [] -> ()
            | all ->
                let parent, key, index = Dst.pick random (Array.ofList all)
                let replacement = randomValue random
                match parent, random.Next 4 with
                | (:? JsonObject as object), 0 -> object.Remove key |> ignore
                | (:? JsonObject as object), _ -> object[key] <- replacement
                | (:? JsonArray as array), 0 -> array.RemoveAt index
                | (:? JsonArray as array), _ -> array[index] <- replacement
                | _ -> ()

    /// Damage the text itself: cut it short, or put a character in that does not belong
    let private damage (random:Random) (text:string) =
        match random.Next 4 with
        | 0 when text.Length > 1 -> text.Substring(0, random.Next text.Length)
        | 1 -> text.Insert(random.Next(text.Length + 1), string (Dst.pick random [| '"'; '{'; '}'; '\\'; '\u0000'; '\ud800'; ',' |]))
        | _ -> text

    [<Fact>]
    let ``RocketManifest.parse gives a result for a manifest that has been changed or damaged, and never throws`` () =
        Dst.run "RocketManifest.parse gives a result" (fun random ->
            let mutable errors = 0
            let mutable oks = 0
            for _ in 1 .. 300 do
                let manifest = randomManifest random
                if random.Next 3 > 0 then mutate random manifest
                let text = manifest.ToJsonString() |> damage random
                match RocketManifest.parse text with
                | Ok _ -> oks <- oks + 1
                | Error _ -> errors <- errors + 1
            // Both outcomes have to happen, or the changes do nothing
            oks |> should be (greaterThan 10)
            errors |> should be (greaterThan 10))

    [<Fact>]
    let ``The same manifest gives the same result however it is written`` () =
        Dst.run "The same manifest gives the same result" (fun random ->
            for _ in 1 .. 100 do
                let manifest = randomManifest random
                let compact = manifest.ToJsonString()
                let indented = manifest.ToJsonString(Text.Json.JsonSerializerOptions(WriteIndented = true))
                describe (RocketManifest.parse compact) |> should equal (describe (RocketManifest.parse indented))
                (match RocketManifest.parse compact with
                 | Ok _ -> ()
                 | Error error -> failwith $"a manifest that Rocket could post was refused: {error.Message}"))

    // A connection that misbehaves

    type private Fault =
        | NoFault
        | IoFailureAt of position:int
        | CancelledAt of position:int

    /// A body that is given out in chunks of random sizes, and that can fail or be cancelled at a byte. It counts the bytes that were read.
    type private ChaosStream(data:byte array, random:Random, fault:Fault, cancel:CancellationTokenSource) =
        inherit Stream()
        let mutable position = 0
        member _.BytesRead = position
        override _.CanRead = true
        override _.CanSeek = false
        override _.CanWrite = false
        override _.Length = int64 data.Length
        override _.Position with get () = int64 position and set _ = raise (NotSupportedException())
        override _.Flush () = ()
        override _.Seek (_, _) = raise (NotSupportedException())
        override _.SetLength _ = raise (NotSupportedException())
        override _.Write (_, _, _) = raise (NotSupportedException())
        member private _.Next (destination:Memory<byte>, token:CancellationToken) =
            token.ThrowIfCancellationRequested()
            match fault with
            | IoFailureAt at when position >= at -> raise (IOException "The connection was reset")
            | CancelledAt at when position >= at ->
                cancel.Cancel()
                token.ThrowIfCancellationRequested()
            | _ -> ()
            // Some reads give one byte, some give a whole buffer, and the last one gives nothing
            let size = min (min destination.Length (data.Length - position)) (Dst.pick random [| 1; 2; 7; 100; 1000; buffer |])
            data.AsSpan(position, size).CopyTo destination.Span
            position <- position + size
            size
        override this.Read (destination:byte[], offset:int, count:int) = this.Next (Memory<byte>(destination, offset, count), CancellationToken.None)
        override this.ReadAsync (destination:Memory<byte>, token:CancellationToken) = ValueTask<int>(this.Next (destination, token))

    /// A valid manifest, padded with spaces at the end to the length asked for when it is shorter
    let private manifestOfLength (random:Random) (length:int) =
        let text = (randomManifest random).ToJsonString()
        let bytes = Encoding.UTF8.GetBytes text
        match bytes.Length >= length with
        | true -> bytes
        | false -> Array.append bytes (Array.create (length - bytes.Length) (byte ' '))

    [<Fact>]
    let ``Request.getRocketManifests gives the same result however the body arrives, and stops reading at the limit`` () =
        Dst.run "Request.getRocketManifests gives the same result" (fun random ->
            for _ in 1 .. 25 do
                // Mostly small bodies, and some at the edge of the limit
                let body =
                    match random.Next 4 with
                    | 0 -> manifestOfLength random (limit - random.Next 3)
                    | 1 -> manifestOfLength random (limit + random.Next 3)
                    | 2 -> manifestOfLength random (limit * 3)
                    | _ -> manifestOfLength random 0
                let ctx = DefaultHttpContext()
                let stream = new ChaosStream(body, random, NoFault, null)
                ctx.Request.Body <- stream
                let result = (Request.getRocketManifests ctx).GetAwaiter().GetResult()
                match body.Length > limit with
                | true ->
                    result |> should equal (Error (RocketManifestError.TooLarge limit) : Result<RocketManifestDocument, RocketManifestError>)
                    // It reads until it is over the limit, so it can be over by one chunk
                    stream.BytesRead |> should be (lessThanOrEqualTo (limit + buffer))
                | false ->
                    describe result |> should equal (describe (RocketManifest.parse (Encoding.UTF8.GetString body)))
                    stream.BytesRead |> should equal body.Length)

    [<Fact>]
    let ``Request.getRocketManifests gives an error for a failed or cancelled connection, and never a result for half a body`` () =
        Dst.run "Request.getRocketManifests gives an error" (fun random ->
            let mutable failures = 0
            let mutable cancellations = 0
            for _ in 1 .. 25 do
                let body = manifestOfLength random (random.Next(200, 60000))
                // A fault before the end of the body is always reached, because the reader needs all of it
                let at = random.Next body.Length
                let fault = Dst.pick random [| IoFailureAt at; CancelledAt at |]
                use cancel = new CancellationTokenSource()
                let ctx = DefaultHttpContext()
                ctx.RequestAborted <- cancel.Token
                ctx.Request.Body <- new ChaosStream(body, random, fault, cancel)
                let result = (Request.getRocketManifests ctx).GetAwaiter().GetResult()
                match fault with
                | IoFailureAt _ ->
                    result |> should equal (Error (RocketManifestError.ConnectionFailed "The connection was reset") : Result<RocketManifestDocument, RocketManifestError>)
                    failures <- failures + 1
                | _ ->
                    result |> should equal (Error RocketManifestError.Cancelled : Result<RocketManifestDocument, RocketManifestError>)
                    cancellations <- cancellations + 1
            failures |> should be (greaterThan 3)
            cancellations |> should be (greaterThan 3))
