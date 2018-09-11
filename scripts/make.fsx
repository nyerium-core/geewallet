#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq
#load "Infra.fs"
open FSX.Infrastructure

let DEFAULT_SOLUTION_FILE = "gwallet.core.sln"

type BinaryConfig =
    | Debug
    | Release
    override self.ToString() =
        sprintf "%A" self

let rec private GatherTarget (args: string list, targetSet: Option<string>): Option<string> =
    match args with
    | [] -> targetSet
    | head::tail ->
        if (targetSet.IsSome) then
            failwith "only one target can be passed to make"
        GatherTarget (tail, Some (head))

let buildConfigContents =
    let buildConfig = FileInfo (Path.Combine (__SOURCE_DIRECTORY__, "build.config"))
    if not (buildConfig.Exists) then
        Console.Error.WriteLine "ERROR: configure hasn't been run yet, run ./configure.sh first"
        Environment.Exit 1

    let skipBlankLines line = not <| String.IsNullOrWhiteSpace line
    let splitLineIntoKeyValueTuple (line:string) =
        let pair = line.Split([|'='|], StringSplitOptions.RemoveEmptyEntries)
        if pair.Length <> 2 then
            failwith "All lines in build.config must conform to format:\n\tkey=value"
        pair.[0], pair.[1]

    let buildConfigContents =
        File.ReadAllLines buildConfig.FullName
        |> Array.filter skipBlankLines
        |> Array.map splitLineIntoKeyValueTuple
        |> Map.ofArray
    buildConfigContents

let DefaultBuildTool () =
    let buildTool = Map.tryFind "BuildTool" buildConfigContents
    if buildTool.IsNone then
        failwith "A BuildTool should have been chosen by the configure script, please report this bug"
    buildTool.Value

let IsGtkSuitableTarget(): bool =
    (Misc.GuessPlatform() = Misc.Platform.Linux &&
        // because old Mono's xbuild cannot build the GTK frontend (because it cannot build NetStandard)
        (not (DefaultBuildTool () = "xbuild")))

let CONSOLE_FRONTEND = "GWallet.Frontend.Console"
let DefaultFrontend () =
    if IsGtkSuitableTarget() then
        "GWallet.Frontend.XF.Gtk"
    else
        CONSOLE_FRONTEND

let GetOrExplain key map =
    match map |> Map.tryFind key with
    | Some k -> k
    | None   -> failwithf "No entry exists in build.config with a key '%s'." key

let prefix = buildConfigContents |> GetOrExplain "Prefix"
let libInstallPath = DirectoryInfo (Path.Combine (prefix, "lib", "gwallet"))
let binInstallPath = DirectoryInfo (Path.Combine (prefix, "bin"))

let launcherScriptPath = FileInfo (Path.Combine (__SOURCE_DIRECTORY__, "bin", "gwallet"))

let wrapperScript = """#!/bin/sh
set -e
exec mono "$TARGET_DIR/$GWALLET_PROJECT.exe" "$@"
"""

let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let PrintNugetVersion () =
    let nugetExe = Path.Combine(rootDir.FullName, ".nuget", "nuget.exe") |> FileInfo
    if not (nugetExe.Exists) then
        false
    else
        let nugetProc = Process.Execute (sprintf "mono %s" nugetExe.FullName, false, true)
        let firstChunk = nugetProc.Output.First()
        match firstChunk with
        | StdOut stdOut ->
            Console.WriteLine stdOut
            true
        | StdErr stdErr ->
            Process.PrintToScreen nugetProc.Output
            Console.WriteLine()
            failwith "nuget process' output contained errors ^"

let BuildSolution buildTool solutionFileName binaryConfig extraOptions =

    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())
    let configOptions =
        match buildConfigContents |> Map.tryFind "DefineConstants" with
        | Some constants -> sprintf "%s;DefineConstants=%s" configOption constants
        | None   -> configOption
    let buildProcess = Process.Execute (sprintf "%s %s %s %s"
                                                buildTool
                                                solutionFileName
                                                configOptions
                                                extraOptions,
                                        true, false)
    if (buildProcess.ExitCode <> 0) then
        Console.Error.WriteLine (sprintf "%s build failed" buildTool)
        PrintNugetVersion() |> ignore
        Environment.Exit 1

let JustBuild binaryConfig =
    Console.WriteLine "Compiling gwallet..."

    let buildTool = DefaultBuildTool()
    BuildSolution buildTool DEFAULT_SOLUTION_FILE binaryConfig String.Empty

    // older mono versions (which only have xbuild, not msbuild) can't compile .NET Standard assemblies
    if IsGtkSuitableTarget() then
        BuildSolution "msbuild" "gwallet.linux.sln" binaryConfig "/t:Restore"

    Directory.CreateDirectory(launcherScriptPath.Directory.FullName) |> ignore
    let wrapperScriptWithPaths =
        wrapperScript.Replace("$TARGET_DIR", libInstallPath.FullName)
                     .Replace("$GWALLET_PROJECT", CONSOLE_FRONTEND)
    File.WriteAllText (launcherScriptPath.FullName, wrapperScriptWithPaths)

let MakeCheckCommand (commandName: string) =
    if (Process.CommandCheck commandName).IsNone then
        Console.Error.WriteLine (sprintf "%s not found, please install it first" commandName)
        Environment.Exit 1

let GetPathToFrontend (binaryConfig: BinaryConfig) =
    Path.Combine ("src", DefaultFrontend(), "bin", binaryConfig.ToString())

let maybeTarget = GatherTarget (Util.FsxArguments(), None)
match maybeTarget with
| None ->
    Console.WriteLine "Building gwallet in DEBUG mode..."
    JustBuild BinaryConfig.Debug

| Some("release") ->
    JustBuild BinaryConfig.Release

| Some "nuget" ->
    Console.WriteLine "This target is for debugging purposes."

    if not (PrintNugetVersion()) then
        Console.Error.WriteLine "Nuget executable has not been downloaded yet, try `make` alone first"
        Environment.Exit 1

| Some("zip") ->
    let zipCommand = "zip"
    MakeCheckCommand zipCommand

    let version = Misc.GetCurrentVersion(rootDir).ToString()

    let release = BinaryConfig.Release
    JustBuild release
    let binDir = "bin"
    Directory.CreateDirectory(binDir) |> ignore

    let zipName = sprintf "gwallet.v.%s.zip" version
    let pathToZip = Path.Combine(binDir, zipName)
    if (File.Exists (pathToZip)) then
        File.Delete (pathToZip)

    let pathToFrontend = GetPathToFrontend release
    let zipLaunch = sprintf "%s -j -r %s %s"
                            zipCommand pathToZip pathToFrontend
    let zipRun = Process.Execute(zipLaunch, true, false)
    if (zipRun.ExitCode <> 0) then
        Console.Error.WriteLine "ZIP compression failed"
        Environment.Exit 1

| Some("check") ->
    Console.WriteLine "Running tests..."
    Console.WriteLine ()

    let nunitCommand = "nunit-console"
    MakeCheckCommand nunitCommand
    let testAssembly = "GWallet.Backend.Tests"
    let testAssemblyPath = Path.Combine(__SOURCE_DIRECTORY__, "..", "src", testAssembly, "bin",
                                        testAssembly + ".dll")
    if not (File.Exists(testAssemblyPath)) then
        failwithf "File not found: %s" testAssemblyPath
    let nunitRun = Process.Execute(sprintf "%s %s" nunitCommand testAssemblyPath,
                                   true, false)
    if (nunitRun.ExitCode <> 0) then
        Console.Error.WriteLine "Tests failed"
        Environment.Exit 1

| Some("install") ->
    Console.WriteLine "Building gwallet in RELEASE mode..."
    JustBuild BinaryConfig.Release

    Console.WriteLine "Installing gwallet..."
    Console.WriteLine ()
    Directory.CreateDirectory(libInstallPath.FullName) |> ignore

    let mainBinariesPath = DirectoryInfo (Path.Combine(__SOURCE_DIRECTORY__, "..",
                                                       "src", CONSOLE_FRONTEND, "bin", "Release"))
    Misc.CopyDirectoryRecursively (mainBinariesPath, libInstallPath)

    let finalPrefixPathOfWrapperScript = FileInfo (Path.Combine(binInstallPath.FullName, launcherScriptPath.Name))
    if not (Directory.Exists(finalPrefixPathOfWrapperScript.Directory.FullName)) then
        Directory.CreateDirectory(finalPrefixPathOfWrapperScript.Directory.FullName) |> ignore
    File.Copy(launcherScriptPath.FullName, finalPrefixPathOfWrapperScript.FullName, true)
    if ((Process.Execute(sprintf "chmod ugo+x %s" finalPrefixPathOfWrapperScript.FullName, false, true)).ExitCode <> 0) then
        failwith "Unexpected chmod failure, please report this bug"

| Some("run") ->
    let fullPathToMono = Process.CommandCheck "mono"
    if (fullPathToMono.IsNone) then
        Console.Error.WriteLine "mono not found? install it first"
        Environment.Exit 1

    let debug = BinaryConfig.Debug
    JustBuild debug

    let pathToFrontend = Path.Combine(GetPathToFrontend debug, DefaultFrontend() + ".exe")

    let proc = System.Diagnostics.Process.Start
                   (fullPathToMono.Value, pathToFrontend)
    proc.WaitForExit()

| Some "update-servers" ->
    let utxoCoinFolder = Path.Combine("src", "GWallet.Backend", "UtxoCoin")

    let btcServersUrl = "https://raw.githubusercontent.com/spesmilo/electrum/master/lib/servers.json"
    let btcServersFile = Path.Combine(utxoCoinFolder, "btc-servers.json")
    let updateBtc = Process.Execute (sprintf "curl -o %s %s" btcServersFile btcServersUrl, true, false)
    if (updateBtc.ExitCode <> 0) then
        Environment.Exit 1

    let ltcServersUrl = "https://raw.githubusercontent.com/pooler/electrum-ltc/master/lib/servers.json"
    let ltcServersFile = Path.Combine(utxoCoinFolder, "ltc-servers.json")
    let updateLtc = Process.Execute (sprintf "curl -o %s %s" ltcServersFile ltcServersUrl, true, false)
    if (updateLtc.ExitCode <> 0) then
        Environment.Exit 1

| Some(someOtherTarget) ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2
