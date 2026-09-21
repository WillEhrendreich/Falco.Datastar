namespace Falco.Datastar

open System
open System.Globalization
open System.Text.Json
open Falco.Markup

/// <summary>
/// Server-side helpers for Rocket, Datastar's web components. Load Rocket with <see cref="Ds.rocketCdnScript"/>.
/// Components are written in JavaScript with <c>rocket(tag, definition)</c>. These helpers cover what the server renders:
/// the props a component reads from its attributes, the signals and actions its children can use, and its template directives.
/// They only write the values you pass, so Rocket's own defaults still apply.
/// https://github.com/starfederation/datastar/tree/v1.0.4/library/src/rocket
/// </summary>
[<AbstractClass; Sealed; RequireQualifiedAccess>]
type Rocket =
    /// <summary>
    /// A signal that belongs to one instance of a component, for use in expressions inside it, e.g. <c>Ds.text (Rocket.local "count")</c>.
    /// Rocket rewrites the name to a path that is unique to the instance, so two instances of the same component do not share the signal.
    /// </summary>
    /// <param name="name">The signal's name. Declare it in the component's setup with <c>$$('name', initialValue)</c></param>
    /// <returns>Expression</returns>
    static member local (name:string) =
        "$$" + name

    /// <summary>
    /// Calls an action of the component that contains the expression, e.g. <c>Ds.onClick (Rocket.call "flip")</c>.
    /// Rocket looks for a component action with that name first, and then for a Datastar action.
    /// </summary>
    /// <param name="name">The action's name. Register it in the component's setup with <c>action('name', fn)</c></param>
    /// <param name="args">Expressions to pass to the action</param>
    /// <returns>Expression</returns>
    static member call (name:string, ?args:string list) =
        let arguments = defaultArg args [] |> String.concat ", "
        $"@{name}({arguments})"

    /// <summary>
    /// Makes a bind, computed, indicator or ref attribute use the page's signal instead of the component's own.
    /// Rocket normally ties these attributes to the component instance. This opts one attribute out, using Rocket's <c>__root</c> modifier.
    /// It does not work on <c>data-signals</c>, which Rocket always ties to the instance.
    /// </summary>
    /// <param name="attribute">The attribute to opt out, e.g. <c>Ds.bind "query"</c></param>
    /// <returns>Attribute</returns>
    static member root (attribute:XmlAttribute) =
        match attribute with
        | KeyValueAttr (key, value) -> KeyValueAttr (key + "__root", value)
        | NonValueAttr key -> NonValueAttr (key + "__root")

    static member private prop (name:string, encoded:string) =
        Attr.create (String.datastarKebab name) (Js.attrEncode encoded)

    /// <summary>
    /// A string prop. The attribute name is the prop name converted the way Rocket converts it, e.g. <c>maxCount</c> becomes <c>max-count</c>.
    /// </summary>
    /// <param name="name">The prop name as the component defines it, e.g. "label"</param>
    /// <param name="value">The text. It is escaped for use in an attribute</param>
    /// <returns>Attribute</returns>
    static member propString (name:string, value:string) =
        Rocket.prop (name, value)

    /// <summary>
    /// A number prop. It is written with the invariant culture, because the browser always reads a dot as the decimal separator, whatever the server's culture is.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">Any numeric type</param>
    /// <returns>Attribute</returns>
    static member propNumber<'T when 'T :> IFormattable> (name:string, value:'T) =
        Rocket.prop (name, (value :> IFormattable).ToString(null, CultureInfo.InvariantCulture))

    /// <summary>
    /// A boolean prop. It is always written, as "true" or "false". If the attribute were left out, the component would use the prop's default, which might be true.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The value</param>
    /// <returns>Attribute</returns>
    static member propBool (name:string, value:bool) =
        Rocket.prop (name, Bool.eitherOr "true" "false" value)

    /// <summary>
    /// A date prop, written as UTC ISO 8601 with milliseconds, the same format as JavaScript's <c>Date.toISOString()</c>.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The moment in time. It is converted to UTC</param>
    /// <returns>Attribute</returns>
    static member propDate (name:string, value:DateTimeOffset) =
        Rocket.prop (name, value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))

    /// <summary>
    /// A structured prop, written as JSON. It works for Rocket's json, array, tuple, object and oneOf codecs.
    /// Property names are camelCase, like the JavaScript objects the component reads them into, unless you pass your own options.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The value to serialize with System.Text.Json. The result is escaped for use in an attribute</param>
    /// <param name="options">Options for the JSON serializer</param>
    /// <returns>Attribute</returns>
    static member propJson<'T> (name:string, value:'T, ?options:JsonSerializerOptions) =
        Rocket.prop (name, JsonSerializer.Serialize<'T>(value, defaultArg options Js.webJsonOptions))

    /// <summary>
    /// A binary prop, written as base64. The component's bin codec decodes it with <c>atob</c>.
    /// </summary>
    /// <param name="name">The prop name as defined in the component</param>
    /// <param name="value">The bytes</param>
    /// <returns>Attribute</returns>
    static member propBin (name:string, value:byte[]) =
        Rocket.prop (name, Convert.ToBase64String value)

    /// <summary>
    /// Repeats the children once for each item in a signal or expression, e.g. <c>Rocket.templateFor (Rocket.local "todos", [ ... ], item = "todo")</c>.
    /// Rocket calls the item <c>item</c> and the index <c>i</c> unless you give other names, and this method only writes names you pass.
    /// The attribute is always <c>data-for</c>, even if you set a different attribute prefix, because Rocket does not support a prefix on its template directives.
    /// </summary>
    /// <param name="source">An expression that gives an array, an iterable or a string</param>
    /// <param name="children">The content of one row. It can use the item and the index by their names</param>
    /// <param name="item">The item's name. If you pass only <paramref name="index"/>, the item is called <c>item</c></param>
    /// <param name="index">The index's name</param>
    /// <returns>Element</returns>
    static member templateFor (source:string, children:XmlNode list, ?item:string, ?index:string) =
        let expression =
            match item, index with
            | None, None -> source
            | Some item', None -> $"{item'} in {source}"
            | None, Some index' -> $"item, {index'} in {source}"
            | Some item', Some index' -> $"{item'}, {index'} in {source}"
        Elem.template [ Attr.create "data-for" (Js.attrEncode expression) ] children

    /// <summary>
    /// Renders the children only while the condition is true. To make a chain, put <see cref="templateElseIf"/> and <see cref="templateElse"/> elements directly after it.
    /// The attribute is always <c>data-if</c>, even if you set a different attribute prefix, because Rocket does not support a prefix on its template directives.
    /// </summary>
    /// <param name="condition">An expression</param>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateIf (condition:string, children:XmlNode list) =
        Elem.template [ Attr.create "data-if" (Js.attrEncode condition) ] children

    /// <summary>
    /// The next branch of a chain that starts with <see cref="templateIf"/>. It must come directly after a templateIf or another templateElseIf.
    /// </summary>
    /// <param name="condition">An expression</param>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateElseIf (condition:string, children:XmlNode list) =
        Elem.template [ Attr.create "data-else-if" (Js.attrEncode condition) ] children

    /// <summary>
    /// The last branch of a chain. It must come directly after a templateIf or a templateElseIf.
    /// </summary>
    /// <param name="children">The content to render</param>
    /// <returns>Element</returns>
    static member templateElse (children:XmlNode list) =
        Elem.template [ Attr.createBool "data-else" ] children

/// <summary>
/// Reads the document that Rocket's <c>publishRocketManifests</c> posts to your server: one entry for every component the page defined,
/// with its props (from their codecs), slots and events. A docs build or a component registry can store it.
/// https://github.com/starfederation/datastar/blob/v1.0.4/library/src/rocket/runtime.ts
/// </summary>
[<RequireQualifiedAccess>]
module RocketManifest =
    let private property (name:string) (element:JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Object ->
            match element.TryGetProperty name with
            | true, value -> ValueSome value
            | false, _ -> ValueNone
        | _ -> ValueNone

    let private required (name:string) (context:string) (element:JsonElement) =
        match property name element with
        | ValueSome value -> Ok value
        | ValueNone -> Error $"The manifest has no {name} in {context}"

    let private text (name:string) (context:string) (element:JsonElement) =
        required name context element
        |> Result.bind (fun value ->
            match value.ValueKind with
            | JsonValueKind.String -> Ok (value.GetString())
            | _ -> Error $"The {name} in {context} is not text")

    let private optionalText (name:string) (element:JsonElement) =
        match property name element with
        | ValueSome value when value.ValueKind = JsonValueKind.String -> ValueSome (value.GetString())
        | _ -> ValueNone

    let private optionalBool (name:string) (element:JsonElement) =
        match property name element with
        | ValueSome value when value.ValueKind = JsonValueKind.True -> ValueSome true
        | ValueSome value when value.ValueKind = JsonValueKind.False -> ValueSome false
        | _ -> ValueNone

    let private items (name:string) (element:JsonElement) =
        match property name element with
        | ValueSome value when value.ValueKind = JsonValueKind.Array -> value.EnumerateArray() |> List.ofSeq
        | _ -> []

    let private readAll (read:JsonElement -> Result<'T, string>) (elements:JsonElement list) =
        elements
        |> List.fold (fun collected element -> collected |> Result.bind (fun readSoFar -> read element |> Result.map (fun item -> item :: readSoFar))) (Ok [])
        |> Result.map List.rev

    let private propType (name:string) =
        match name with
        | "string" -> RocketPropType.String
        | "number" -> RocketPropType.Number
        | "boolean" -> RocketPropType.Boolean
        | "date" -> RocketPropType.Date
        | "json" -> RocketPropType.Json
        | "js" -> RocketPropType.Js
        | "binary" -> RocketPropType.Binary
        | "array" -> RocketPropType.Array
        | "tuple" -> RocketPropType.Tuple
        | "object" -> RocketPropType.Object
        | "oneOf" -> RocketPropType.OneOf
        | "custom" -> RocketPropType.Custom
        | other -> RocketPropType.Other other

    let private eventKind (name:string) =
        match name with
        | "event" -> RocketEventKind.Event
        | "custom-event" -> RocketEventKind.CustomEvent
        | other -> RocketEventKind.Other other

    let private readDocs (element:JsonElement) =
        match property "docs" element with
        | ValueSome docs when docs.ValueKind = JsonValueKind.Object ->
            ValueSome { Description = optionalText "description" docs
                        Label = optionalText "label" docs
                        Control = optionalText "control" docs
                        Placeholder = optionalText "placeholder" docs }
        | _ -> ValueNone

    let private readProp (tag:string) (element:JsonElement) =
        let context = $"a prop of {tag}"
        text "name" context element
        |> Result.bind (fun name ->
            text "attribute" context element
            |> Result.bind (fun attribute ->
                text "type" context element
                |> Result.bind (fun typeName ->
                    required "default" context element
                    |> Result.map (fun defaultValue ->
                        { Name = name
                          Attribute = attribute
                          Type = propType typeName
                          Default = defaultValue.Clone()
                          Required = optionalBool "required" element |> ValueOption.defaultValue false
                          Values =
                            match property "values" element with
                            | ValueSome values when values.ValueKind = JsonValueKind.Array ->
                                ValueSome (values.EnumerateArray() |> Seq.map (fun value -> value.Clone()) |> List.ofSeq)
                            | _ -> ValueNone
                          Docs = readDocs element }))))

    let private readSlot (tag:string) (element:JsonElement) =
        text "name" $"a slot of {tag}" element
        |> Result.map (fun name -> { Name = name; Description = optionalText "description" element })

    let private readEvent (tag:string) (element:JsonElement) =
        text "name" $"an event of {tag}" element
        |> Result.map (fun name ->
            { Name = name
              Kind = optionalText "kind" element |> ValueOption.map eventKind |> ValueOption.defaultValue RocketEventKind.Event
              Bubbles = optionalBool "bubbles" element
              Composed = optionalBool "composed" element
              Description = optionalText "description" element })

    let private readComponent (element:JsonElement) =
        text "tag" "a component" element
        |> Result.bind (fun tag ->
            readAll (readProp tag) (items "props" element)
            |> Result.bind (fun props ->
                readAll (readSlot tag) (items "slots" element)
                |> Result.bind (fun slots ->
                    readAll (readEvent tag) (items "events" element)
                    |> Result.map (fun events -> { Tag = tag; Props = props; Slots = slots; Events = events }))))

    let private supportedVersion = 1

    let private readDocument (root:JsonElement) =
        required "version" "the manifest" root
        |> Result.bind (fun version ->
            match version.ValueKind, version.TryGetInt32() with
            | JsonValueKind.Number, (true, number) when number = supportedVersion -> Ok number
            | JsonValueKind.Number, (true, number) ->
                Error $"This library reads Rocket manifest version {supportedVersion}, but the document is version {number}"
            | _ -> Error "The version in the manifest is not a number")
        |> Result.bind (fun version ->
            text "generatedAt" "the manifest" root
            |> Result.bind (fun generatedAt ->
                match DateTimeOffset.TryParse(generatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None) with
                | true, moment -> Ok moment
                | false, _ -> Error "The generatedAt in the manifest is not a date")
            |> Result.bind (fun generatedAt ->
                required "components" "the manifest" root
                |> Result.bind (fun components -> readAll readComponent (components.EnumerateArray() |> List.ofSeq))
                |> Result.map (fun components -> { Version = version; GeneratedAt = generatedAt; Components = components })))

    /// <summary>
    /// Reads the JSON that <c>publishRocketManifests</c> posted. It returns an error message when the text is not JSON,
    /// when a required property is missing, or when the document has a version other than 1.
    /// </summary>
    /// <param name="json">The body of the request</param>
    let parse (json:string) : Result<RocketManifestDocument, string> =
        try
            use document = JsonDocument.Parse json
            readDocument document.RootElement
        with :? JsonException as error -> Error $"The manifest is not valid JSON: {error.Message}"
