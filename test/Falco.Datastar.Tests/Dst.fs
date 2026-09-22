namespace Falco.Datastar.Tests

open System

/// Deterministic simulation testing. A randomized test takes all of its randomness from one seed, so a failure can be replayed exactly.
///
/// - By default each test runs the seeds 1 to 25, so a normal run is fast and the same every time.
/// - DST_SEEDS=2000 runs the seeds 1 to 2000, to look for rare cases.
/// - DST_SEED=417 runs only seed 417, to replay a failure.
///
/// A failure message names the seed and the command that replays it.
module Dst =
    let private variable (name:string) =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> None
        | value -> Some (int value)

    /// The seeds that a test runs
    let seeds () =
        match variable "DST_SEED", variable "DST_SEEDS" with
        | Some seed, _ -> [ seed ]
        | None, Some count -> [ 1 .. count ]
        | None, None -> [ 1 .. 25 ]

    /// Runs the test once for each seed, with a Random that starts from the seed.
    /// <c>testName</c> is a part of the name of the xunit test, so that the message can say how to run it again.
    let run (testName:string) (test: Random -> unit) =
        for seed in seeds () do
            try
                test (Random seed)
            with error ->
                raise (Exception($"DST failure with seed {seed}. Replay it with: DST_SEED={seed} dotnet test --filter \"FullyQualifiedName~{testName}\"{Environment.NewLine}{error.Message}", error))

    /// Picks one of the choices
    let pick (random:Random) (choices: 'a array) = choices.[random.Next choices.Length]
