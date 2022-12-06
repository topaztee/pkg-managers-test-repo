﻿using ReactiveUI;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Splat;


namespace Paket.VisualStudio.Commands.PackageGui
{
    /// <summary>
    /// This helps with keeping design time view models in sync with real view models
    /// </summary>
    public interface IAddPackageViewModel
    {
        string SearchText { get; set; }
        NugetResult SelectedPackage { get; set; }
        ReactiveList<NugetResult> NugetResults { get; }

        ReactiveCommand<NugetResult> SearchNuget { get; }
        ReactiveCommand<System.Reactive.Unit> AddPackage { get; }
        IObservable<Logging.Trace> PaketTrace { get; }
        LoadingState AddPackageState { get; }
    }

    public class NugetResult
    {
        public string PackageName { get; set; }
    }

    public enum LoadingState
    {
        Loading,
        Success,
        Failure
    }

    public class AddPackageViewModel : ReactiveObject, IAddPackageViewModel
    {
        private readonly Paket.Dependencies _dependenciesFile;
        private readonly IObservable<Logging.Trace> _paketTraceFunObservable;

        public IObservable<Logging.Trace> PaketTrace
        {
            get { return _paketTraceFunObservable; }
        }
        string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText,value);
            }
        }

        NugetResult _selectedPackage;
        public NugetResult SelectedPackage
        {
            get { return _selectedPackage; }
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedPackage, value);
            }
        }

        private ObservableAsPropertyHelper<LoadingState> _addPackageState;

        public LoadingState AddPackageState
        {
            get { return _addPackageState.Value; }
        }


        private ReactiveList<NugetResult> _nugetResults  = new ReactiveList<NugetResult>();
        public ReactiveList<NugetResult> NugetResults { get { return _nugetResults; } }
        public ReactiveCommand<NugetResult> SearchNuget { get; private set; }


        public ReactiveCommand<System.Reactive.Unit> AddPackage { get; private set; }

        public AddPackageViewModel(
            Func<string ,IObservable<string>> searchForPackages,
            Action<NugetResult> addPackageCallback,
            IObservable<Logging.Trace> paketTraceFunObservable)
        {

            var logger = new DebugLogger() {Level = LogLevel.Debug};
            Splat.Locator.CurrentMutable.RegisterConstant(logger, typeof(ILogger));


            _paketTraceFunObservable = paketTraceFunObservable;
            SearchNuget =
                ReactiveCommand.CreateAsyncObservable(
                    this.ObservableForProperty(x => x.SearchText)
                        .Select(x => !string.IsNullOrEmpty(SearchText)),
                    _ =>
                        searchForPackages(SearchText)
                            .Select(x => new NugetResult {PackageName = x}));
                            

            //TODO: Localization
            var errorMessage = "NuGet packages couldn't be loaded.";
            var errorResolution = "You may not have internet or NuGet may be down.";
            
            SearchNuget.ThrownExceptions
                .Log(this, "Search NuGet exception:", e => e.ToString())
                .Select(ex => new UserError(errorMessage, errorResolution))
                .SelectMany(UserError.Throw)
                .Subscribe();

            SearchNuget.IsExecuting
               .Where(isExecuting => isExecuting)
               .ObserveOn(RxApp.MainThreadScheduler)
               .Subscribe(_ =>
               {
                   NugetResults.Clear();
               });

            SearchNuget.Subscribe(NugetResults.Add);


            AddPackage = ReactiveCommand.CreateAsyncTask(
                this.WhenAnyValue(x => x.SelectedPackage).Select(x => x != null),
                _ => Task.Run(() => addPackageCallback(SelectedPackage)));

            _addPackageState = AddPackage
                .ThrownExceptions
                .Log(this, "Add Package exception:", e => e.ToString())
                .Select(_ => LoadingState.Failure)
                .Merge(
                   AddPackage
                    .IsExecuting
                    .Where(isExecuting => isExecuting)
                    .Select(_ => LoadingState.Loading))
                .Merge(
                     AddPackage
                        .Select(_ => LoadingState.Success))
                .ToProperty(this, v => v.AddPackageState, out _addPackageState);

            _addPackageState
                .ThrownExceptions
                .Log(this, "Add package state exception:", e => e.ToString())
                .Select(ex => new UserError("", errorResolution))
                .SelectMany(UserError.Throw)
                .Subscribe();
            
            this.ObservableForProperty(x => x.SearchText)
                .Where(x => !string.IsNullOrEmpty(SearchText))
                .Throttle(TimeSpan.FromMilliseconds(250))
                .InvokeCommand(SearchNuget);
        }
    }
}
