﻿/// Contains methods for NuGet conversion
module Paket.NuGetConvert

open Paket
open System
open System.IO
open System.Xml
open Paket.Domain
open Paket.Logging
open Paket.Xml
open Paket.PackageSources
open Paket.Requirements
open Chessie.ErrorHandling
open Paket.PackagesConfigFile
open InstallProcess

type CredsMigrationMode =
    | Encrypt
    | Plaintext
    | Selective

    static member Parse(s : string) =
        match s with
        | "encrypt" -> ok Encrypt
        | "plaintext" -> ok Plaintext
        | "selective" -> ok Selective
        | _ ->  InvalidCredentialsMigrationMode s |> fail

    static member ToAuthentication mode sourceName auth =
        match mode, auth with
        | Encrypt, Credentials userPass ->
            Credentials userPass
        | Plaintext, Credentials userPass ->
            Credentials userPass
        | Selective, Credentials userPass ->
            let question =
                sprintf "Credentials for source '%s': " sourceName  +
                    "[encrypt and save in config (Yes) " +
                    sprintf "| save as plaintext in %s (No)]" Constants.DependenciesFileName

            match Utils.askYesNo question with
            | true -> Credentials userPass
            | false -> Credentials userPass
        | _ -> failwith "invalid auth"

/// Represents type of NuGet packages.config file
type NugetPackagesConfigType = ProjectLevel | SolutionLevel

/// Represents NuGet packages.config file
type NugetPackagesConfig = {
    File: FileInfo
    Packages: NugetPackage list
    Type: NugetPackagesConfigType
}

let private tryGetValue key (node : XmlNode) =
    node
    |> getNodes "add"
    |> List.tryFind (getAttribute "key" >> (=) (Some key))
    |> Option.bind (getAttribute "value")

let private getKeyValueList (node : XmlNode) =
    node
    |> getNodes "add"
    |> List.choose (fun node ->
        match node |> getAttribute "key", node |> getAttribute "value" with
        | Some key, Some value -> Some(key, value)
        | _ -> None)

type NugetConfig =
    { PackageSources : Map<string, string * Auth option>
      PackageRestoreEnabled : bool
      PackageRestoreAutomatic : bool }

    static member Empty =
        { PackageSources = Map.empty
          PackageRestoreEnabled = false
          PackageRestoreAutomatic = false }

    static member GetConfigNode (file : FileInfo) =
        try
            let doc = XmlDocument()
            ( use f = File.OpenRead file.FullName
              doc.Load(f))
            (doc |> getNode "configuration").Value |> ok
        with _ ->
            file
            |> NugetConfigFileParseError
            |> fail

    static member OverrideConfig nugetConfig (configNode : XmlNode) =
        let getAuth key =
            let getAuth' authNode =
                let userName = authNode |> tryGetValue "Username"
                let clearTextPass = authNode |> tryGetValue "ClearTextPassword"
                let encryptedPass = authNode |> tryGetValue "Password"

                match userName, encryptedPass, clearTextPass with
                | Some userName, Some encryptedPass, _ ->
                    Some(Credentials{Username = userName; Password = ConfigFile.DecryptNuget encryptedPass; Type = AuthType.Basic})
                | Some userName, _, Some clearTextPass ->
                    Some(Credentials{Username = userName; Password = clearTextPass; Type = AuthType.Basic})
                | _ -> None

            configNode
            |> getNode "packageSourceCredentials"
            |> optGetNode (XmlConvert.EncodeLocalName key)
            |> Option.bind getAuth'

        let disabledSources =
            configNode |> getNode "disabledPackageSources"
            |> Option.toList
            |> List.collect getKeyValueList
            |> List.filter (fun (_,disabled) -> disabled.Equals("true", StringComparison.OrdinalIgnoreCase))
            |> List.map fst
            |> Set.ofList

        let sources =
            configNode |> getNode "packageSources"
            |> Option.toList
            |> List.collect getKeyValueList
            |> List.map (fun (key,value) -> key, (String.quoted value, getAuth key))
            |> List.filter (fun (key,_) -> key.Contains "NuGetFallbackFolder" |> not)
            |> Map.ofList

        { PackageSources =
            match configNode.SelectSingleNode("//packageSources/clear") with
            | null -> Map.fold (fun acc k v -> Map.add k v acc) nugetConfig.PackageSources sources
            | _ -> sources
            |> Map.filter (fun k _ -> Set.contains k disabledSources |> not)
          PackageRestoreEnabled =
            match configNode |> getNode "packageRestore" |> Option.bind (tryGetValue "enabled") with
            | Some value -> bool.Parse(value)
            | None -> nugetConfig.PackageRestoreEnabled
          PackageRestoreAutomatic =
            match configNode |> getNode "packageRestore" |> Option.bind (tryGetValue "automatic") with
            | Some value -> bool.Parse(value)
            | None -> nugetConfig.PackageRestoreAutomatic }

type NugetEnv =
    { RootDirectory : DirectoryInfo
      NuGetConfig : NugetConfig
      NuGetConfigFiles : FileInfo list
      NuGetProjectFiles : (ProjectFile * NugetPackagesConfig option) list
      NuGetTargets : FileInfo option
      NuGetExe : FileInfo option }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module NugetEnv =
    let create rootDirectory configFiles targets exe config packagesFiles =
        { RootDirectory = rootDirectory
          NuGetConfig = config
          NuGetConfigFiles = configFiles
          NuGetProjectFiles = packagesFiles
          NuGetTargets = targets
          NuGetExe = exe
        }

    let readNugetConfig(rootDirectory : DirectoryInfo) =
        DirectoryInfo(Path.Combine(rootDirectory.FullName, ".nuget"))
        |> List.unfold (fun di ->
                match di with
                | null -> None
                | _ -> Some(FileInfo(Path.Combine(di.FullName, "nuget.config")), di.Parent))
        |> List.rev
        |> List.append [FileInfo(Path.Combine(Constants.AppDataFolder, "nuget", "nuget.config"))]
        |> List.filter (fun fi -> fi.Exists)
        |> List.fold (fun config file ->
                        config
                        |> bind (fun config ->
                            file
                            |> NugetConfig.GetConfigNode
                            |> lift (NugetConfig.OverrideConfig config)))
                        (ok NugetConfig.Empty)

    let readNuGetPackages(rootDirectory : DirectoryInfo) =
        let readSingle(file : FileInfo) =
            try
                { File = file
                  Type = if file.Directory.Name = ".nuget" then SolutionLevel else ProjectLevel
                  Packages = PackagesConfigFile.Read file.FullName }
                |> ok
            with _ -> fail (NugetPackagesConfigParseError file)

        let readPackages (projectFile : ProjectFile) : Result<ProjectFile * option<NugetPackagesConfig>, DomainMessage> =
            let path = Path.Combine(Path.GetDirectoryName(projectFile.FileName), Constants.PackagesConfigFile)
            if File.Exists path then
                FileInfo(path) |> readSingle |> lift Some
            else
                Result.Succeed None
            |> lift (fun configFile -> (projectFile,configFile))

        let projectFiles = ProjectFile.FindAllProjects rootDirectory.FullName

        projectFiles
        |> Array.map readPackages
        |> collect
    let read (rootDirectory : DirectoryInfo) = trial {
        let configs = FindAllFiles(rootDirectory.FullName, "nuget.config") |> Array.toList
        let targets = FindAllFiles(rootDirectory.FullName, "nuget.targets") |> Array.tryHead
        let exe = FindAllFiles(rootDirectory.FullName, "nuget.exe") |> Array.tryHead
        let! config = readNugetConfig rootDirectory
        let! packages = readNuGetPackages rootDirectory

        return create rootDirectory configs targets exe config packages
    }

type ConvertResultR =
    { NuGetEnv : NugetEnv
      PaketEnv : PaketEnv
      SolutionFiles : SolutionFile [] }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ConvertResultR =
    let create nugetEnv paketEnv solutionFiles =
        { NuGetEnv = nugetEnv
          PaketEnv = paketEnv
          SolutionFiles = solutionFiles }

let createPackageRequirement sources (packageName, versionRequirement, restrictions) dependenciesFileName =
     { Name = PackageName packageName
       VersionRequirement = versionRequirement
       ResolverStrategyForDirectDependencies = None
       ResolverStrategyForTransitives = None
       Settings = { InstallSettings.Default with FrameworkRestrictions = restrictions }
       Parent = PackageRequirementSource.DependenciesFile dependenciesFileName
       Sources = sources
       Kind = PackageRequirementKind.Package
       TransitivePrereleases = false
       Graph = Set.empty }

let private isFSharpProject (projectFileName:string) =
    projectFileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)

let private addFSharpCoreToDependenciesIfRequired nugetEnv packages =
    let hasFSharpProject =
        nugetEnv.NuGetProjectFiles
        |> Seq.exists (fun (prj,_) -> isFSharpProject prj.FileName)
    let hasFSharpCorePackage =
        packages
        |> Seq.exists (fun (n,_,_,_) -> "fsharp.core".Equals(n, StringComparison.OrdinalIgnoreCase))
    if hasFSharpProject && not hasFSharpCorePackage then
        let fsCore = ("FSharp.Core", VersionRequirement.AllReleases,FrameworkRestriction.NoRestriction, NugetPackageKind.Package)
        fsCore :: packages
    else
        packages

let createDependenciesFileR (rootDirectory : DirectoryInfo) nugetEnv mode =

    let dependenciesFileName = Path.Combine(rootDirectory.FullName, Constants.DependenciesFileName)

    let allVersionsGroupped =
        nugetEnv.NuGetProjectFiles
        |> List.collect (fun (pf,c) ->
            c
            |> Option.map (fun x -> x.Packages)
            |> Option.toList
            |> List.concat
            |> List.append (ProjectFile.dotNetCorePackages pf)
            |> List.append (ProjectFile.cliTools pf))
        |> List.groupBy (fun p -> p.Id)

    let findDistinctPackages selector =
        allVersionsGroupped
        |> List.map (fun (name, packages) -> name, packages |> selector)
        |> List.sortBy (fun (name,_) -> name.ToLower())

    let findWarnings searchBy message =
        for name, versions in findDistinctPackages searchBy do
            if List.length versions > 1 then
              traceWarnfn message name versions

    findWarnings (List.choose (fun p -> match p.VersionRequirement.Range with Specific v -> Some v | _ -> None) >> List.distinct >> List.map string)
        "Package %s is referenced multiple times in different versions: %A. Paket will choose the latest one."
    findWarnings (List.map (fun p -> p.TargetFramework) >> List.distinct >> List.choose id >> List.map string)
        "Package %s is referenced multiple times with different target frameworks : %A. Paket may disregard target framework."

    let latestVersions =
        findDistinctPackages (List.map (fun p -> p.VersionRequirement, p.TargetFramework, p.Kind) >> List.distinct)
        |> List.map (fun (name, versions) ->
            let latestVersion, _, _ = versions |> List.maxBy (fun (x,_,_) -> x)
            let kind =
                if versions |> List.exists (fun (_,_,x) -> x = NugetPackageKind.DotnetCliTool) then
                    NugetPackageKind.DotnetCliTool
                else
                    NugetPackageKind.Package
            let restrictions =
                match versions with
                | [ version, targetFramework, clitool ] ->
                    targetFramework
                    |> Option.toList
                    |> List.map (fun fw ->
                        let restrictions, problems = Requirements.parseRestrictionsLegacy false fw
                        for framework in problems |> Seq.choose (fun x -> x.Framework) do
                            if not (framework.StartsWith "_") then
                                Logging.traceErrorfn "Could not detect any platforms from '%s' in %O %O, please tell the package authors" framework name version
                        restrictions)
                | _ -> []
            let restrictions =
                if List.isEmpty restrictions then FrameworkRestriction.NoRestriction
                else restrictions |> Seq.fold FrameworkRestriction.combineRestrictionsWithOr FrameworkRestriction.EmptySet
            name, latestVersion, restrictions, kind)

    let packages =
        match nugetEnv.NuGetExe with
        | Some _ -> ("NuGet.CommandLine",VersionRequirement.AllReleases,FrameworkRestriction.NoRestriction, NugetPackageKind.Package) :: latestVersions
        | _ -> latestVersions
        |> addFSharpCoreToDependenciesIfRequired nugetEnv

    let read() =
        let addPackages dependenciesFile =
            packages
            |> List.map (fun (name, vr, restrictions, kind) ->
                let settings = { InstallSettings.Default with FrameworkRestrictions = ExplicitRestriction restrictions }
                // FSharp.Core > 5.0.0 include the xml docs in a content file, so we want to default to ignoring those for new users to prevent confusion
                let settings = if name.Equals("FSharp.Core", StringComparison.OrdinalIgnoreCase) then { settings with OmitContent = Some ContentCopySettings.Omit } else settings
                Constants.MainDependencyGroup, PackageName name, vr, settings, kind)
            |> List.fold (fun (dependenciesFile:DependenciesFile) (groupName, packageName,versionRequirement,installSettings,kind) ->
                let reqKind =
                    match kind with
                    | NugetPackageKind.Package -> PackageRequirementKind.Package
                    | NugetPackageKind.DotnetCliTool -> PackageRequirementKind.DotnetCliTool
                dependenciesFile.Add(groupName, packageName,versionRequirement.Range,installSettings, reqKind)) dependenciesFile
        try
            DependenciesFile.ReadFromFile dependenciesFileName
            |> ok
        with e -> DependenciesFileParseError (FileInfo dependenciesFileName, e) |> fail
        |> lift addPackages

    let create() =
        let sources =
            if nugetEnv.NuGetConfig.PackageSources = Map.empty then [ Constants.DefaultNuGetStream, None ]
            else
                (nugetEnv.NuGetConfig.PackageSources
                 |> Map.toList
                 |> List.map snd)
            |> List.map (fun (n, auth) -> n, auth |> Option.map (CredsMigrationMode.ToAuthentication mode n))
            |> List.filter (fun (key,v) -> key.Contains "NuGetFallbackFolder" |> not)
            |> List.map (fun (source, auth) ->
                            try PackageSource.Parse(source,AuthProvider.ofFunction (fun _ -> auth)) |> ok
                            with _ -> source |> PackageSourceParseError |> fail
                            |> successTee PackageSource.WarnIfNoConnection)

            |> collect

        sources
        |> lift (fun sources ->
            let sourceLines = sources |> List.map (fun s -> DependenciesFileSerializer.sourceString(s.ToString()))
            let packageLines =
                packages
                |> List.mapi (fun i (name,vr,restr, kind) ->
                    let vr = createPackageRequirement sources (name, vr, ExplicitRestriction restr) (dependenciesFileName,i)
                    let reqKind =
                        match kind with
                        | NugetPackageKind.Package -> PackageRequirementKind.Package
                        | NugetPackageKind.DotnetCliTool -> PackageRequirementKind.DotnetCliTool
                    DependenciesFileSerializer.packageString reqKind vr.Name vr.VersionRequirement vr.ResolverStrategyForTransitives vr.Settings)

            let newLines = sourceLines @ [""] @ packageLines |> Seq.toArray

            Paket.DependenciesFile(DependenciesFileParser.parseDependenciesFile dependenciesFileName false newLines))

    if File.Exists dependenciesFileName then read() else create()
    |> lift (fun d -> d.SimplifyFrameworkRestrictions())

let convertPackagesConfigToReferencesFile projectFileName packagesConfig =
    let referencesFile = ProjectFile.FindOrCreateReferencesFile(FileInfo projectFileName)

    packagesConfig.Packages
    |> List.map ((fun p -> p.Id) >> PackageName)
    |> List.fold (fun (r : ReferencesFile) packageName -> r.AddNuGetReference(Constants.MainDependencyGroup,packageName))
                 referencesFile

let convertDependenciesConfigToReferencesFile projectFileName dependencies =
    let referencesFile = ProjectFile.FindOrCreateReferencesFile(FileInfo projectFileName)

    dependencies
    |> List.fold (fun (r : ReferencesFile) (packageName,_) -> r.AddNuGetReference(Constants.MainDependencyGroup,packageName))
                 referencesFile

let addFSharpCoreToReferencesIfRequired projectFileName references =

    let containsFSharpCore references =
        let allPackageReferences =
            references.Groups |> Seq.collect (fun g -> g.Value.NugetPackages)
        let hasFSharpCore =
            allPackageReferences |> Seq.exists (fun n -> n.Name.CompareString = "fsharp.core")
        hasFSharpCore

    if isFSharpProject projectFileName && not (containsFSharpCore references) then
        references.AddNuGetReference (GroupName MainGroup, PackageName "FSharp.Core")
    else
        references

let convertProjects nugetEnv =
    [for project,packagesConfig in nugetEnv.NuGetProjectFiles do
        let packagesAndIds =
            packagesConfig
            |> Option.map (fun x -> x.Packages)
            |> Option.toList
            |> List.concat
            |> List.choose (fun p ->
                match p.VersionRequirement.Range with
                | VersionRange.Specific v -> Some(p.Id, v)
                | _ -> None )

        project.ReplaceNuGetPackagesFile()
        project.RemoveNuGetTargetsEntries()
        project.RemoveNugetAnalysers(packagesAndIds)
        project.RemoveImportAndTargetEntries(packagesAndIds)
        project.RemoveNuGetPackageImportStamp()

        let referencesFileFromPackagesConfig =
            packagesConfig
            |> Option.map (convertPackagesConfigToReferencesFile project.FileName)

        let packageReferences =
            project.GetPackageReferences()

        let cliReferences =
            project.GetCliToolReferences()

        let referencesFile =
            match referencesFileFromPackagesConfig with
            | Some x -> x
            | None -> project.FindOrCreateReferencesFile()

        let referencesFile =
            packageReferences @ cliReferences
            |> List.fold
                (fun (rf: ReferencesFile) pr -> rf.AddNuGetReference(Constants.MainDependencyGroup, PackageName pr))
                referencesFile
            |> addFSharpCoreToReferencesIfRequired project.FileName

        project.RemovePackageReferenceEntries()
        project.RemoveCliToolReferenceEntries()

        yield project, referencesFile]

let createPaketEnv rootDirectory nugetEnv credsMirationMode = trial {
    let! depFile = createDependenciesFileR rootDirectory nugetEnv credsMirationMode
    let convertedProjects = convertProjects nugetEnv
    return PaketEnv.create rootDirectory depFile None convertedProjects
}

let updateSolutions (rootDirectory : DirectoryInfo) =
    let dependenciesFileName = Path.Combine(rootDirectory.FullName, Constants.DependenciesFileName)
    let solutions =
        FindAllFiles(rootDirectory.FullName, "*.sln")
        |> Array.map (fun fi -> SolutionFile(fi.FullName))

    for solution in solutions do
        let dependenciesFileRef = createRelativePath solution.FileName dependenciesFileName
        solution.RemoveNuGetEntries()
        solution.AddPaketFolder(dependenciesFileRef, None)

    solutions

let createResult(rootDirectory, nugetEnv, credsMirationMode) = trial {
    let! paketEnv = createPaketEnv rootDirectory nugetEnv credsMirationMode
    return ConvertResultR.create nugetEnv paketEnv (updateSolutions rootDirectory)
}

let convertR rootDirectory force credsMigrationMode = trial {
    let! credsMigrationMode =
        defaultArg
            (credsMigrationMode |> Option.map CredsMigrationMode.Parse)
            (ok Encrypt)

    let! nugetEnv = NugetEnv.read rootDirectory

    let! rootDirectory =
        if force then ok rootDirectory
        else PaketEnv.ensureNotExists rootDirectory

    return! createResult(rootDirectory, nugetEnv, credsMigrationMode)
}

let replaceNuGetWithPaket initAutoRestore installAfter result =
    let remove (fi : FileInfo) =
        tracefn "Removing %s" fi.FullName
        fi.Delete()

    for f in result.NuGetEnv.NuGetConfigFiles do
        remove f

    result.NuGetEnv.NuGetProjectFiles
    |> List.map (fun (_,n) -> n |> Option.map (fun x -> x.File))
    |> List.choose id
    |> List.iter remove

    result.NuGetEnv.NuGetTargets |> Option.iter remove
    result.NuGetEnv.NuGetExe
    |> Option.iter
            (fun nugetExe ->
            remove nugetExe
            traceWarnfn "Removed %s and added %s as dependency instead. Please check all paths."
                nugetExe.FullName "NuGet.CommandLine")

    match result.NuGetEnv.NuGetTargets ++ result.NuGetEnv.NuGetExe with
    | Some fi when fi.Directory.EnumerateFileSystemInfos() |> Seq.isEmpty ->
        fi.Directory.Delete()
    | _ -> ()

    result.PaketEnv.DependenciesFile.Save()
    for project, referencesFile in result.PaketEnv.Projects do
        project.Save true
        referencesFile.Save()

    for s in result.SolutionFiles do
        s.Save()

    let autoVSPackageRestore =
        result.NuGetEnv.NuGetConfig.PackageRestoreAutomatic &&
        result.NuGetEnv.NuGetConfig.PackageRestoreEnabled

    if initAutoRestore && (autoVSPackageRestore || result.NuGetEnv.NuGetTargets.IsSome) then
        try
            VSIntegration.TurnOnAutoRestore result.PaketEnv |> returnOrFail
        with
        | exn ->
            traceWarnfn "Could not enable auto restore%sMessage: %s" Environment.NewLine exn.Message

    UpdateProcess.Update(
        result.PaketEnv.DependenciesFile.FileName,
        { UpdaterOptions.Default with
            Common = { InstallerOptions.Default with
                           Force = true
                           Redirects = BindingRedirectsSettings.On }
            NoInstall = not installAfter })
