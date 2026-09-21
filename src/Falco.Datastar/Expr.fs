namespace Falco.Datastar

open System
open System.Text.RegularExpressions

// Signals, expressions and statements written in F# instead of JavaScript strings.
//
// This is one slice on purpose. Each type has a private case, so the only way to get a value is through the functions next to it.
// That rules out a signal whose name Datastar cannot read, and an expression of the wrong type. Expr.unsafeRaw and Stmt.unsafeRaw are the exception.
// The text inside an Expr or a Stmt is always escaped for an attribute.

/// Where a signal lives. It decides whether Datastar sends the signal to the server.
[<RequireQualifiedAccess>]
type SignalScope =
    /// The signal exists in the browser only. Datastar never sends it, because its name starts with an underscore.
    /// The Tao of Datastar says to use signals for user interactions, such as toggling an element, and this is the scope for that.
    | Browser
    /// Datastar sends the signal to the server with every request.
    /// The Tao says to use these for sending new state to the backend, for example by binding them to form inputs.
    | Server
    /// The signal belongs to one instance of a Rocket component, and is written with two dollar signs.
    | RocketComponent

module internal SignalNameText =
    let camelCase (name:string) =
        Regex.Replace(name, "-+([A-Za-z0-9])", fun found -> found.Groups.[1].Value.ToUpperInvariant())

    /// The name with the first letter of each part in lower case
    let lowerFirstOfEachPart (name:string) =
        name.Split('.') |> Array.map (fun part -> match part.Length with | 0 -> part | _ -> string (Char.ToLowerInvariant part.[0]) + part.Substring 1) |> String.concat "."

/// Why a name cannot be used for a signal. Signal.tryCreate returns it. Its Message says what to write instead.
[<RequireQualifiedAccess>]
type SignalNameError =
    /// The name is empty
    | Blank
    /// The name has a hyphen, which an expression reads as minus
    | HasHyphen of name:string
    /// A part of the name starts with an underscore, which Datastar reads as a signal that stays in the browser
    | StartsWithUnderscore of scope:SignalScope * name:string
    /// A part of the name starts with a capital letter, which HTML makes lower case in an attribute name
    | StartsWithCapital of name:string
    /// The name has two underscores in a row, which Datastar reads as the start of a modifier
    | HasDoubleUnderscore of name:string
    /// A part of the name ends with an underscore, which Datastar reads together with the underscores of a modifier
    | EndsWithUnderscore of name:string
    /// The name is not a series of parts made of letters, digits and underscores, separated by dots
    | NotAPath of name:string
    with
    member error.Message =
        match error with
        | SignalNameError.Blank -> "A signal needs a name. Give it a camelCase name such as 'menuOpen', or a dotted one such as 'form.firstName'."
        | SignalNameError.HasHyphen name ->
            $"The signal name '{name}' has a hyphen. In an expression Datastar reads a hyphen as minus. Use camelCase instead, for example '{SignalNameText.camelCase name}'."
        | SignalNameError.StartsWithUnderscore (SignalScope.Server, name) ->
            $"The signal name '{name}' starts with an underscore. Datastar keeps a signal like that in the browser and never sends it to the server. Use Signal.browser for a browser-only signal, and write the name without the underscore. Use a name without an underscore for a signal you want to send."
        | SignalNameError.StartsWithUnderscore (SignalScope.Browser, name) ->
            $"The signal name '{name}' starts with an underscore. Signal.browser adds the underscore itself, so write the name without it."
        | SignalNameError.StartsWithUnderscore (SignalScope.RocketComponent, name) ->
            $"The signal name '{name}' starts with an underscore. Write the name without it."
        | SignalNameError.StartsWithCapital name ->
            $"The signal name '{name}' has a part that starts with a capital letter. Datastar reads the name from an attribute name, and HTML makes attribute names lower case, so the signal would be called '{name.ToLowerInvariant()}' there but '{name}' in an expression. Start each part with a lower case letter, for example '{SignalNameText.lowerFirstOfEachPart name}'."
        | SignalNameError.HasDoubleUnderscore name ->
            let before = name.Substring(0, name.IndexOf("__", StringComparison.Ordinal))
            $"The signal name '{name}' has two underscores in a row. Datastar reads '__' in an attribute name as the start of a modifier, so it would read '{name}' as '{before}' with a modifier. Use a single underscore, or camelCase."
        | SignalNameError.EndsWithUnderscore name ->
            $"The signal name '{name}' has a part that ends with an underscore. Datastar would read that underscore together with the two that start a modifier, and lose part of the name. Remove the trailing underscore."
        | SignalNameError.NotAPath name ->
            $"The signal name '{name}' cannot be read as a signal path. Start each part with a lower case letter, use only letters, digits and underscores in it, and separate parts with a dot, for example 'form.firstName'."

/// A signal that holds a value of type 'T. Create it with Signal.browser, Signal.server or Signal.rocket.
type Signal<'T> = private { Scope: SignalScope; Name: string }

module internal SignalName =
    /// A part starts with a lower case letter. An underscore must have a letter or a digit after it, so no part ends with one and none has two in a row.
    /// \z is the end of the text. A dollar sign would also match before a final line break.
    let internal segment = Regex(@"^[a-z](?:[A-Za-z0-9]|_(?=[A-Za-z0-9]))*\z", RegexOptions.Compiled)

    let internal startsWithUnderscore (name:string) =
        name.Split('.') |> Array.exists (fun part -> part.StartsWith '_')

    /// The reason a name cannot be used for a signal, or nothing when it can
    let problem (scope:SignalScope) (name:string) =
        let parts = match isNull name with | true -> [||] | false -> name.Split('.')
        match String.IsNullOrWhiteSpace name with
        | true -> ValueSome SignalNameError.Blank
        | false when name.Contains '-' -> ValueSome (SignalNameError.HasHyphen name)
        | false when startsWithUnderscore name -> ValueSome (SignalNameError.StartsWithUnderscore (scope, name))
        | false when name.Contains "__" -> ValueSome (SignalNameError.HasDoubleUnderscore name)
        | false when parts |> Array.exists (fun part -> part.Length > 0 && Char.IsAsciiLetterUpper part.[0]) -> ValueSome (SignalNameError.StartsWithCapital name)
        | false when parts |> Array.exists (fun part -> part.EndsWith '_') -> ValueSome (SignalNameError.EndsWithUnderscore name)
        | false when parts |> Array.forall segment.IsMatch |> not -> ValueSome (SignalNameError.NotAPath name)
        | false -> ValueNone

[<RequireQualifiedAccess>]
module Signal =
    /// Creates a signal, or says why the name cannot be used. Datastar reads a signal name in an expression and in an attribute name,
    /// so the name has to work in both.
    let tryCreate<'T> (scope:SignalScope) (name:string) : Result<Signal<'T>, SignalNameError> =
        match SignalName.problem scope name with
        | ValueSome error -> Error error
        | ValueNone -> Ok { Scope = scope; Name = name }

    let internal orRaise (created:Result<Signal<'T>, SignalNameError>) =
        match created with
        | Ok signal -> signal
        | Error error -> raise (ArgumentException(error.Message, "name"))

    /// A signal that stays in the browser. Datastar never sends it to the server. Write the name without an underscore, because this adds it.
    /// Use it for what the user does on the page, such as an open menu or a loading indicator. Raises an ArgumentException when the name cannot be used;
    /// use tryCreate to get the reason as a value.
    let browser<'T> (name:string) : Signal<'T> = tryCreate<'T> SignalScope.Browser name |> orRaise

    /// A signal that Datastar sends to the server with every request. Use it for new state that the backend needs, such as the value of a form input.
    /// Raises an ArgumentException when the name cannot be used; use tryCreate to get the reason as a value.
    let server<'T> (name:string) : Signal<'T> = tryCreate<'T> SignalScope.Server name |> orRaise

    /// A signal that belongs to one instance of a Rocket component. Raises an ArgumentException when the name cannot be used; use tryCreate to get the reason as a value.
    let rocket<'T> (name:string) : Signal<'T> = tryCreate<'T> SignalScope.RocketComponent name |> orRaise

    let scope (signal:Signal<'T>) = signal.Scope

    /// The name as Datastar writes it in an attribute name: with the underscore for a browser signal.
    let path (signal:Signal<'T>) =
        match signal.Scope with
        | SignalScope.Browser -> "_" + signal.Name
        | SignalScope.Server
        | SignalScope.RocketComponent -> signal.Name

    /// The name as an expression writes it: $name, or $$name for a Rocket component signal.
    let reference (signal:Signal<'T>) =
        match signal.Scope with
        | SignalScope.RocketComponent -> "$$" + signal.Name
        | SignalScope.Browser
        | SignalScope.Server -> "$" + path signal

/// An expression that has a value of type 'T. Build it with the functions in the Expr module.
type Expr<'T> = private Expr of string

/// Something that is done and gives no value, such as setting a signal or calling a backend action. Build it with the functions in the Stmt module.
type Stmt = private Stmt of string

module internal BackendActionExpression =
    let render (options:RequestOptions voption) (action:BackendAction) =
        let name, url =
            match action with
            | Get url -> "get", url
            | Post url -> "post", url
            | Put url -> "put", url
            | Patch url -> "patch", url
            | Delete url -> "delete", url
            | Query url -> "query", url
        match options with
        | ValueNone -> $"@{name}({Js.stringLiteral url})"
        | ValueSome options' -> $"@{name}({Js.stringLiteral url},{RequestOptions.Serialize options'})"

module internal FilterActionExpression =
    let setAll<'T> (value:'T) (filter:SignalsFilter) =
        match SignalsFilter.IsNone filter with
        | true -> $"@setAll({Js.literal value})"
        | false -> $"@setAll({Js.literal value}, {SignalsFilter.Serialize filter})"

    let toggleAll (filter:SignalsFilter) =
        match SignalsFilter.IsNone filter with
        | true -> "@toggleAll()"
        | false -> $"@toggleAll({SignalsFilter.Serialize filter})"

[<RequireQualifiedAccess>]
module Expr =
    /// The JavaScript that Datastar runs, escaped and ready to put in an attribute. It is not meant for a script element, where the escapes would show.
    let toString (Expr text) = text

    let int (value:int) : Expr<int> = Expr (match value < 0 with | true -> $"({value})" | false -> string value)

    /// A number. NaN and the infinities are JavaScript literals too, so this never raises.
    let float (value:float) : Expr<float> =
        let text = Js.number value
        Expr (match text.StartsWith '-' with | true -> $"({text})" | false -> text)

    let bool (value:bool) : Expr<bool> = Expr (match value with | true -> "true" | false -> "false")

    /// Text. It is escaped, so it stays text whatever it contains.
    let string (value:string) : Expr<string> = Expr (Js.stringLiteral value)

    /// The value of a signal.
    let read (signal:Signal<'T>) : Expr<'T> = Expr (Signal.reference signal)

    /// A name or a number such as evt.key, $count or 2.5. It needs no parentheses when another expression uses it.
    let private isSingleName (javaScript:string) = Regex.IsMatch(javaScript, @"^[\w$@][\w$.@]*\z")

    /// JavaScript that this library has no typed function for. Nothing checks that it is valid JavaScript, or that it has the type you give it.
    /// It is escaped for the attribute, and put in parentheses when it is more than a name, so an operator around it applies to all of it.
    /// Never build it from text a user can change.
    let unsafeRaw<'T> (javaScript:string) : Expr<'T> =
        let safe = Js.attrEncode javaScript
        Expr (match isSingleName javaScript with | true -> safe | false -> $"({safe})")

    let internal binary (operator:string) (Expr left) (Expr right) = Expr $"({left} {operator} {right})"

    // The operators are written with spaces on purpose: Datastar reads $a-1 as a signal called a-1.

    /// Adds two numbers. To join text, use concat.
    let add (left:Expr<'n>) (right:Expr<'n>) : Expr<'n> when 'n :> IFormattable = binary "+" left right
    let subtract (left:Expr<'n>) (right:Expr<'n>) : Expr<'n> when 'n :> IFormattable = binary "-" left right
    let multiply (left:Expr<'n>) (right:Expr<'n>) : Expr<'n> when 'n :> IFormattable = binary "*" left right

    let private isWholeNumber<'n> () =
        [ typeof<int>; typeof<int64>; typeof<int16>; typeof<sbyte>; typeof<byte>; typeof<uint16>; typeof<uint32>; typeof<uint64>; typeof<bigint> ]
        |> List.contains typeof<'n>

    /// Divides two numbers. JavaScript has one kind of number, so 7 / 2 is 3.5 there. When the numbers are whole, such as int, the result is cut to a whole number too.
    let divide (left:Expr<'n>) (right:Expr<'n>) : Expr<'n> when 'n :> IFormattable =
        match binary "/" left right, isWholeNumber<'n> () with
        | Expr quotient, true -> Expr $"Math.trunc{quotient}"
        | quotient, false -> quotient
    let remainder (left:Expr<'n>) (right:Expr<'n>) : Expr<'n> when 'n :> IFormattable = binary "%" left right

    let greater (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : comparison = binary ">" left right
    let less (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : comparison = binary "<" left right
    let atLeast (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : comparison = binary ">=" left right
    let atMost (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : comparison = binary "<=" left right
    let equal (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : equality = binary "===" left right
    let notEqual (left:Expr<'n>) (right:Expr<'n>) : Expr<bool> when 'n : equality = binary "!==" left right

    let andAlso (left:Expr<bool>) (right:Expr<bool>) : Expr<bool> = binary "&&" left right
    let orElse (left:Expr<bool>) (right:Expr<bool>) : Expr<bool> = binary "||" left right
    let negate (Expr value:Expr<bool>) : Expr<bool> = Expr $"(!{value})"

    /// One of two values, chosen by a condition. Both values have the same type.
    let ifElse (Expr condition:Expr<bool>) (Expr whenTrue:Expr<'T>) (Expr whenFalse:Expr<'T>) : Expr<'T> =
        Expr $"({condition} ? {whenTrue} : {whenFalse})"

    /// Joins text.
    let concat (parts:Expr<string> list) : Expr<string> =
        match parts with
        | [] -> Expr "''"
        | _ -> Expr ("(" + String.Join(" + ", parts |> List.map toString) + ")")

    /// Any value as text.
    let toText (Expr value:Expr<'T>) : Expr<string> = Expr $"String({value})"

    /// Reads a value without subscribing to the signals in it, so a change to them does not run the expression again.
    /// Use it inside Ds.effect or Ds.computed. https://data-star.dev/reference/actions#peek
    let peek (Expr value:Expr<'T>) : Expr<'T> = Expr $"@peek(() => {value})"

[<RequireQualifiedAccess>]
module Stmt =
    /// The JavaScript that Datastar runs, escaped and ready to put in an attribute. It is not meant for a script element, where the escapes would show.
    let toString (Stmt text) = text

    /// Sets a signal to an expression of the same type.
    let set (signal:Signal<'T>) (Expr value:Expr<'T>) : Stmt = Stmt $"{Signal.reference signal} = {value}"

    /// Turns a true signal into false and a false one into true.
    let toggle (signal:Signal<bool>) : Stmt = Stmt $"{Signal.reference signal} = !{Signal.reference signal}"

    /// Runs the statements one after another.
    let all (statements:Stmt list) : Stmt =
        match statements with
        | [] -> raise (ArgumentException("Stmt.all needs at least one statement. An attribute with an empty expression makes Datastar throw ValueRequired, so leave the attribute out instead.", "statements"))
        | _ -> Stmt (String.Join("; ", statements |> List.map toString))

    /// JavaScript that this library has no typed function for. Nothing checks that it is valid JavaScript. It is escaped for the attribute.
    /// Never build it from text a user can change.
    let unsafeRaw (javaScript:string) : Stmt = Stmt (Js.attrEncode javaScript)

    /// Sets every signal whose path starts with the prefix, e.g. "form.", to the value. https://data-star.dev/reference/actions#setall
    let setAll (prefix:string) (value:'T) : Stmt = Stmt (FilterActionExpression.setAll value (SignalsFilter.Prefix prefix))

    /// Sets every signal that matches the filter to the value, or every signal when there is no filter.
    let setAllWhere (filter:SignalsFilter) (value:'T) : Stmt = Stmt (FilterActionExpression.setAll value filter)

    /// Toggles every signal whose path starts with the prefix. https://data-star.dev/reference/actions#toggleall
    let toggleAll (prefix:string) : Stmt = Stmt (FilterActionExpression.toggleAll (SignalsFilter.Prefix prefix))

    /// Toggles every signal that matches the filter, or every signal when there is no filter.
    let toggleAllWhere (filter:SignalsFilter) : Stmt = Stmt (FilterActionExpression.toggleAll filter)

    let get (url:string) : Stmt = Stmt (BackendActionExpression.render ValueNone (Get url))
    let post (url:string) : Stmt = Stmt (BackendActionExpression.render ValueNone (Post url))
    let put (url:string) : Stmt = Stmt (BackendActionExpression.render ValueNone (Put url))
    let patch (url:string) : Stmt = Stmt (BackendActionExpression.render ValueNone (Patch url))
    let delete (url:string) : Stmt = Stmt (BackendActionExpression.render ValueNone (Delete url))
    let query (url:string) : Stmt = Stmt (BackendActionExpression.render ValueNone (Query url))

    let getWith (url:string) (options:RequestOptions) : Stmt = Stmt (BackendActionExpression.render (ValueSome options) (Get url))
    let postWith (url:string) (options:RequestOptions) : Stmt = Stmt (BackendActionExpression.render (ValueSome options) (Post url))
    let putWith (url:string) (options:RequestOptions) : Stmt = Stmt (BackendActionExpression.render (ValueSome options) (Put url))
    let patchWith (url:string) (options:RequestOptions) : Stmt = Stmt (BackendActionExpression.render (ValueSome options) (Patch url))
    let deleteWith (url:string) (options:RequestOptions) : Stmt = Stmt (BackendActionExpression.render (ValueSome options) (Delete url))
    let queryWith (url:string) (options:RequestOptions) : Stmt = Stmt (BackendActionExpression.render (ValueSome options) (Query url))
