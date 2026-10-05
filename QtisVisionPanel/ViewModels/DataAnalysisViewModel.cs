using QtisVisionPanel.Cls_Config;
using QtisVisionPanel.Cls_Config.Calss_structure;
using QtisVisionPanel.Database;
using QtisVisionPanel.Models;
using QtisVisionPanel.ServerMessage;
using QtisVisionPanel.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QtisVisionPanel.ViewModels
{
    public class DataAnalysisViewModel : INotifyPropertyChanged, IDisposable
    {
        private const double BarMaxWidth = 320;
        private const double PieCenter = 110;
        private const double PieRadius = 88;
        private const double TrendWidth = 600;
        private const double TrendHeight = 180;
        private const double TrendAxisLabelWidth = 72;
        private const string ActiveRecipeFilterValue = "__ACTIVE_RECIPE__";

        private readonly ProductionAnalyticsRepository _repository = new ProductionAnalyticsRepository();
        private readonly DispatcherTimer _refreshTimer;
        private readonly Dictionary<DataAnalysisCardType, DataAnalysisTrendCard> _trendCardMap;
        private readonly Dictionary<DataAnalysisCardType, string> _cardTitles;
        private readonly Dictionary<DataAnalysisCardType, MeasurementTrendDefinition> _measurementDefinitions;
        private static readonly DataAnalysisSnapshotCacheService SnapshotCache = new DataAnalysisSnapshotCacheService();
        private CancellationTokenSource _refreshCts;

        private bool _isBusy;
        private bool _isEditModeActive;
        private string _statusMessage;
        private DateTime _startDate;
        private DateTime _endDate;
        private DataAnalysisRecipeOption _selectedRecipe;
        private DataAnalysisAvailableCardOption _selectedAvailableCard;
        private string _totalBarColorText;
        private string _goodBarColorText;
        private string _noGoodBarColorText;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<DataAnalysisRecipeOption> RecipeOptions { get; } = new ObservableCollection<DataAnalysisRecipeOption>();
        public ObservableCollection<DataAnalysisAvailableCardOption> AvailableCards { get; } = new ObservableCollection<DataAnalysisAvailableCardOption>();
        public ObservableCollection<DataAnalysisBarItem> ProductionBars { get; } = new ObservableCollection<DataAnalysisBarItem>();
        public ObservableCollection<DataAnalysisDefectItem> DefectItems { get; } = new ObservableCollection<DataAnalysisDefectItem>();
        public ObservableCollection<DataAnalysisTrendCard> TrendCards { get; } = new ObservableCollection<DataAnalysisTrendCard>();

        public ICommand ApplyFiltersCommand { get; }
        public ICommand AddCardCommand { get; }
        public ICommand RemoveCardCommand { get; }
        public ICommand SaveAppearanceCommand { get; }
        public ICommand ToggleEditModeCommand { get; }

        public string Title => GetMessage("Sub_entry_DataAnalysisTitle", "Data Analysis");
        public string Subtitle => GetMessage("Sub_entry_DataAnalysisSubtitle", "Production overview, defect distribution and measurement trends");
        public string FilterTitle => GetMessage("Sub_entry_DataAnalysisFilters", "Filters");
        public string StartDateLabel => GetMessage("Sub_entry_DataAnalysisStartDate", "Start date");
        public string EndDateLabel => GetMessage("Sub_entry_DataAnalysisEndDate", "End date");
        public string RecipeLabel => GetMessage("Sub_entry_DataAnalysisRecipe", "Recipe");
        public string RefreshLabel => GetMessage("Sub_entry_DataAnalysisRefresh", "Refresh");
        public string ExportPdfLabel => GetMessage("Sub_entry_DataAnalysisExportPdf", "Export PDF");
        public string ColorsLabel => GetMessage("Sub_entry_DataAnalysisColors", "Bar colors");
        public string AddCardLabel => GetMessage("Sub_entry_DataAnalysisAddCard", "Add graph");
        public string SaveLayoutLabel => GetMessage("Sub_entry_DataAnalysisSaveLayout", "Save dashboard");
        public string ProductionOverviewLabel => GetMessage("Sub_entry_DataAnalysisProductionOverview", "Production overview");
        public string DefectDistributionLabel => GetMessage("Sub_entry_DataAnalysisDefectDistribution", "Defect distribution");
        public string TotalLabel => GetMessage("Sub_entry_DataAnalysisTotal", "Total");
        public string GoodLabel => GetMessage("Sub_entry_DataAnalysisGood", "Good");
        public string NoGoodLabel => GetMessage("Sub_entry_DataAnalysisNoGood", "NoGood");
        public string UnclassifiedLabel => GetMessage("Sub_entry_Unclassified", "Non classificato");
        public string TotalBarColorLabel => GetMessage("Sub_entry_DataAnalysisTotalBarColor", "Total color");
        public string GoodBarColorLabel => GetMessage("Sub_entry_DataAnalysisGoodBarColor", "Good color");
        public string NoGoodBarColorLabel => GetMessage("Sub_entry_DataAnalysisNoGoodBarColor", "NoGood color");
        public string NoRecipeFilterLabel => GetMessage("Sub_entry_DataAnalysisAllRecipes", "All recipes");
        public string ActiveRecipeFilterLabel => GetMessage("Sub_entry_DataAnalysisActiveRecipe", "Active recipe inspections");
        public string EmptyDataLabel => GetMessage("Sub_entry_DataAnalysisEmpty", "No data in selected range.");

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value)
                    return;

                _isBusy = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsAdministrator => UserSession.IsAdministrator;

        public bool CanEditDashboard => UserSession.IsAdministrator && _isEditModeActive;

        public string EditOrSaveLabel => _isEditModeActive
            ? GetMessage("Sub_entry_DataAnalysisSaveLayout", "Save Layout")
            : GetMessage("Sub_entry_DataAnalysisEditDashboard", "Edit Dashboard");

        public string ExportSubtitle
        {
            get
            {
                string recipe = SelectedRecipe?.Label ?? NoRecipeFilterLabel;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1:dd/MM/yyyy HH:mm} - {2:dd/MM/yyyy HH:mm} | {3}: {4}",
                    FilterTitle,
                    StartDate,
                    EndDate,
                    RecipeLabel,
                    recipe);
            }
        }

        public bool HasDefectData => DefectItems.Any();

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value)
                    return;

                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public DateTime StartDate
        {
            get => _startDate;
            set
            {
                var merged = value.Date + _startDate.TimeOfDay;
                if (_startDate == merged)
                    return;

                _startDate = merged;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartTimeText));
            }
        }

        public string StartTimeText
        {
            get => _startDate.ToString("HH:mm", CultureInfo.InvariantCulture);
            set
            {
                if (TryParseTime(value, out TimeSpan t))
                {
                    var merged = _startDate.Date + t;
                    if (_startDate == merged)
                        return;

                    _startDate = merged;
                    OnPropertyChanged(nameof(StartDate));
                    OnPropertyChanged(nameof(StartTimeText));
                }
            }
        }

        public DateTime EndDate
        {
            get => _endDate;
            set
            {
                var merged = value.Date + _endDate.TimeOfDay;
                if (_endDate == merged)
                    return;

                _endDate = merged;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EndTimeText));
            }
        }

        public string EndTimeText
        {
            get => _endDate.ToString("HH:mm", CultureInfo.InvariantCulture);
            set
            {
                if (TryParseTime(value, out TimeSpan t))
                {
                    var merged = _endDate.Date + t;
                    if (_endDate == merged)
                        return;

                    _endDate = merged;
                    OnPropertyChanged(nameof(EndDate));
                    OnPropertyChanged(nameof(EndTimeText));
                }
            }
        }

        public DataAnalysisRecipeOption SelectedRecipe
        {
            get => _selectedRecipe;
            set
            {
                if (_selectedRecipe == value)
                    return;

                _selectedRecipe = value;
                OnPropertyChanged();
            }
        }

        public DataAnalysisAvailableCardOption SelectedAvailableCard
        {
            get => _selectedAvailableCard;
            set
            {
                if (_selectedAvailableCard == value)
                    return;

                _selectedAvailableCard = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string TotalBarColorText
        {
            get => _totalBarColorText;
            set
            {
                if (_totalBarColorText == value)
                    return;

                _totalBarColorText = value;
                OnPropertyChanged();
            }
        }

        public string GoodBarColorText
        {
            get => _goodBarColorText;
            set
            {
                if (_goodBarColorText == value)
                    return;

                _goodBarColorText = value;
                OnPropertyChanged();
            }
        }

        public string NoGoodBarColorText
        {
            get => _noGoodBarColorText;
            set
            {
                if (_noGoodBarColorText == value)
                    return;

                _noGoodBarColorText = value;
                OnPropertyChanged();
            }
        }

        public DataAnalysisViewModel()
        {
            _cardTitles = new Dictionary<DataAnalysisCardType, string>
            {
                [DataAnalysisCardType.ProductionOverview] = ProductionOverviewLabel,
                [DataAnalysisCardType.DefectPie] = DefectDistributionLabel,
                [DataAnalysisCardType.HeightTrend] = GetMessage("Sub_entry_DataAnalysisHeightTrend", "Height trend"),
                [DataAnalysisCardType.ThreeDHeightTrend] = GetMessage("Sub_entry_DataAnalysis3DHeightTrend", "3D height trend"),
                [DataAnalysisCardType.WidthTrend] = GetMessage("Sub_entry_DataAnalysisWidthTrend", "3D width trend"),
                [DataAnalysisCardType.LengthTrend] = GetMessage("Sub_entry_DataAnalysisLengthTrend", "3D length trend"),
                [DataAnalysisCardType.RejectRateTrend] = GetMessage("Sub_entry_DataAnalysisRejectRateTrend", "Reject rate trend"),
                [DataAnalysisCardType.ProductionRateTrend] = GetMessage("Sub_entry_DataAnalysisProductionRateTrend", "Production rate trend")
            };

            _measurementDefinitions = new Dictionary<DataAnalysisCardType, MeasurementTrendDefinition>
            {
                [DataAnalysisCardType.HeightTrend] = new MeasurementTrendDefinition
                {
                    CardType = DataAnalysisCardType.HeightTrend,
                    Candidates = new[] { "HeightMeasureValue", "HeigthMeasureValue" },
                    IsInspectionEnabled = status => status?.Height == true || status == null,
                    Subtitle = GetMessage("Sub_entry_DataAnalysisTrendHeightSubtitle", "Height distribution over time"),
                    ReferenceResolver = recipe =>
                    {
                        if (recipe?.recipeParamSide == null)
                            return null;

                        double nominal = recipe.recipeParamSide.Min_Heigth_value;
                        double tolerance = recipe.recipeParamSide.Heigth_toll;
                        if (nominal <= 0 && tolerance <= 0)
                            return null;

                        return new TrendReferenceRange
                        {
                            NominalValue = nominal,
                            LowerToleranceValue = nominal - tolerance,
                            UpperToleranceValue = nominal + tolerance
                        };
                    }
                },
                [DataAnalysisCardType.ThreeDHeightTrend] = new MeasurementTrendDefinition
                {
                    CardType = DataAnalysisCardType.ThreeDHeightTrend,
                    Candidates = new[] { "ThreeDHeightMeasureValue" },
                    IsInspectionEnabled = status => status?.ThreeDHeight == true || status == null,
                    Subtitle = GetMessage("Sub_entry_DataAnalysisTrend3DHeightSubtitle", "3D height distribution over time"),
                    ReferenceResolver = recipe => CreateThreeDReference(recipe?.recipeParamTop3D?.ThreeDHeightNominalValue, recipe?.recipeParamTop3D?.ThreeDHeightTolerance)
                },
                [DataAnalysisCardType.WidthTrend] = new MeasurementTrendDefinition
                {
                    CardType = DataAnalysisCardType.WidthTrend,
                    Candidates = new[] { "ThreeDWidthMeasureValue" },
                    IsInspectionEnabled = status => status?.ThreeDWidth == true || status == null,
                    Subtitle = GetMessage("Sub_entry_DataAnalysisTrendWidthSubtitle", "3D width distribution over time"),
                    ReferenceResolver = recipe => CreateThreeDReference(recipe?.recipeParamTop3D?.ThreeDWidthNominalValue, recipe?.recipeParamTop3D?.ThreeDWidthTolerance)
                },
                [DataAnalysisCardType.LengthTrend] = new MeasurementTrendDefinition
                {
                    CardType = DataAnalysisCardType.LengthTrend,
                    Candidates = new[] { "ThreeDLengthMeasureValue" },
                    IsInspectionEnabled = status => status?.ThreeDLength == true || status == null,
                    Subtitle = GetMessage("Sub_entry_DataAnalysisTrendLengthSubtitle", "3D length distribution over time"),
                    ReferenceResolver = recipe => CreateThreeDReference(recipe?.recipeParamTop3D?.ThreeDLengthNominalValue, recipe?.recipeParamTop3D?.ThreeDLengthTolerance)
                }
            };

            _trendCardMap = new Dictionary<DataAnalysisCardType, DataAnalysisTrendCard>
            {
                [DataAnalysisCardType.HeightTrend] = new DataAnalysisTrendCard
                {
                    CardType = DataAnalysisCardType.HeightTrend,
                    Title = _cardTitles[DataAnalysisCardType.HeightTrend],
                    ValueUnit = " mm"
                },
                [DataAnalysisCardType.ThreeDHeightTrend] = new DataAnalysisTrendCard
                {
                    CardType = DataAnalysisCardType.ThreeDHeightTrend,
                    Title = _cardTitles[DataAnalysisCardType.ThreeDHeightTrend],
                    ValueUnit = " mm"
                },
                [DataAnalysisCardType.WidthTrend] = new DataAnalysisTrendCard
                {
                    CardType = DataAnalysisCardType.WidthTrend,
                    Title = _cardTitles[DataAnalysisCardType.WidthTrend],
                    ValueUnit = " mm"
                },
                [DataAnalysisCardType.LengthTrend] = new DataAnalysisTrendCard
                {
                    CardType = DataAnalysisCardType.LengthTrend,
                    Title = _cardTitles[DataAnalysisCardType.LengthTrend],
                    ValueUnit = " mm"
                },
                [DataAnalysisCardType.RejectRateTrend] = new DataAnalysisTrendCard
                {
                    CardType = DataAnalysisCardType.RejectRateTrend,
                    Title = _cardTitles[DataAnalysisCardType.RejectRateTrend],
                    ShowMinMax = false,
                    ValueUnit = "%"
                },
                [DataAnalysisCardType.ProductionRateTrend] = new DataAnalysisTrendCard
                {
                    CardType = DataAnalysisCardType.ProductionRateTrend,
                    Title = _cardTitles[DataAnalysisCardType.ProductionRateTrend],
                    ShowMinMax = false,
                    ValueUnit = " pcs/h"
                }
            };

            foreach (var item in _trendCardMap.Values)
            {
                TrendCards.Add(item);
            }

            _startDate = DateTime.Today;
            _endDate = DateTime.Today.AddHours(23).AddMinutes(59);

            LoadDashboardConfig();
            RebuildAvailableCards();

            ApplyFiltersCommand = new RelayCommand(async _ => await RefreshAllAsync(), _ => !IsBusy);
            AddCardCommand = new RelayCommand(_ => AddSelectedCard(), _ => CanEditDashboard && SelectedAvailableCard != null);
            RemoveCardCommand = new RelayCommand(param => RemoveCard(param), _ => CanEditDashboard);
            SaveAppearanceCommand = new RelayCommand(async _ => await SaveDashboardConfigAsync(), _ => CanEditDashboard && !IsBusy);
            ToggleEditModeCommand = new RelayCommand(
                async _ =>
                {
                    if (_isEditModeActive)
                        await SaveDashboardConfigAsync();
                    else
                    {
                        _isEditModeActive = true;
                        NotifyEditModeChanged();
                    }
                },
                _ => UserSession.IsAdministrator && !IsBusy);

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMinutes(GetRefreshMinutes())
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            UserSession.OnRoleChanged += UserSession_OnRoleChanged;

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await Task.Yield();

            ApplySnapshot(SnapshotCache.TryGetLatestSnapshot(), true);

            await ReloadRecipesAsync().ConfigureAwait(true);

            var range = ResolveRange();
            string recipeName = ResolveEffectiveRecipeName();
            ApplySnapshot(SnapshotCache.TryGetSnapshot(range.start, range.end, recipeName), true);

            _ = RefreshAllAsync();
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            await RefreshAllAsync().ConfigureAwait(true);
        }

        private void UserSession_OnRoleChanged(object sender, EventArgs e)
        {
            _isEditModeActive = false;
            OnPropertyChanged(nameof(IsAdministrator));
            OnPropertyChanged(nameof(CanEditDashboard));
            OnPropertyChanged(nameof(EditOrSaveLabel));
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task RefreshAllAsync()
        {
            if (IsBusy)
                return;

            try
            {
                _refreshCts?.Cancel();
                _refreshCts?.Dispose();
                _refreshCts = new CancellationTokenSource();

                IsBusy = true;
                await ReloadRecipesAsync().ConfigureAwait(true);

                var range = ResolveRange();
                string recipeName = ResolveEffectiveRecipeName();
                var recipeContext = await LoadRecipeContextAsync(recipeName).ConfigureAwait(true);
                if (_refreshCts.IsCancellationRequested)
                    return;

                var snapshot = await CreateSnapshotAsync(range.start, range.end, recipeName, recipeContext).ConfigureAwait(true);
                if (_refreshCts.IsCancellationRequested)
                    return;

                SnapshotCache.StoreSnapshot(snapshot);
                ApplySnapshot(snapshot, false);
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Data analysis refresh failed: {ex.Message}");
                StatusMessage = GetMessage("Sub_entry_DataAnalysisStatusError", "Data analysis refresh failed.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ReloadRecipesAsync()
        {
            var range = ResolveRange();
            var recipes = await _repository.GetRecipesInRangeAsync(range.start, range.end).ConfigureAwait(true);
            bool hadSelection = SelectedRecipe != null;
            string selectedValue = SelectedRecipe?.Value;

            RecipeOptions.Clear();
            RecipeOptions.Add(new DataAnalysisRecipeOption
            {
                Label = NoRecipeFilterLabel,
                Value = null
            });
            RecipeOptions.Add(new DataAnalysisRecipeOption
            {
                Label = ActiveRecipeFilterLabel,
                Value = ActiveRecipeFilterValue
            });

            foreach (string recipe in recipes)
            {
                RecipeOptions.Add(new DataAnalysisRecipeOption
                {
                    Label = recipe,
                    Value = recipe
                });
            }

            SelectedRecipe = (hadSelection
                    ? RecipeOptions.FirstOrDefault(r => string.Equals(r.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? RecipeOptions.FirstOrDefault(r => string.Equals(r.Value, ActiveRecipeFilterValue, StringComparison.OrdinalIgnoreCase))
                ?? RecipeOptions.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Value))
                ?? RecipeOptions.FirstOrDefault();
        }

        private async Task<AnalyticsDashboardSnapshot> CreateSnapshotAsync(
            DateTime start,
            DateTime end,
            string recipeName,
            RecipeContextResult recipeContext)
        {
            var productionTask = _repository.GetProductionOverviewAsync(start, end, recipeName);
            var defectsTask = _repository.GetDefectDistributionAsync(start, end, recipeName);
            var heightTask = ResolveTrendTaskAsync(DataAnalysisCardType.HeightTrend, start, end, recipeName, recipeContext);
            var threeDHeightTask = ResolveTrendTaskAsync(DataAnalysisCardType.ThreeDHeightTrend, start, end, recipeName, recipeContext);
            var widthTask = ResolveTrendTaskAsync(DataAnalysisCardType.WidthTrend, start, end, recipeName, recipeContext);
            var lengthTask = ResolveTrendTaskAsync(DataAnalysisCardType.LengthTrend, start, end, recipeName, recipeContext);
            var rejectRateTask = _repository.GetRejectRateTrendAsync(start, end, recipeName);
            var productionRateTask = _repository.GetProductionRateTrendAsync(start, end, recipeName);

            await Task.WhenAll(productionTask, defectsTask, heightTask, threeDHeightTask, widthTask, lengthTask, rejectRateTask, productionRateTask).ConfigureAwait(true);

            string rejectSubtitle = GetMessage("Sub_entry_DataAnalysisRejectRateSubtitle", "% rejected pieces per 15-minute interval");
            string rateSubtitle = GetMessage("Sub_entry_DataAnalysisProductionRateSubtitle", "Estimated pieces per hour per 15-minute interval");

            return new AnalyticsDashboardSnapshot
            {
                Start = start,
                End = end,
                CreatedAt = DateTime.Now,
                RecipeName = recipeName,
                ResolvedRecipePath = recipeContext?.ResolvedRecipePath,
                InspectionStatus = recipeContext?.InspectionStatus,
                ProductionOverview = productionTask.Result,
                Defects = new Dictionary<string, int>(defectsTask.Result, StringComparer.OrdinalIgnoreCase),
                Trends = new Dictionary<DataAnalysisCardType, AnalyticsTrendSnapshot>
                {
                    [DataAnalysisCardType.HeightTrend] = heightTask.Result.ToSnapshot(),
                    [DataAnalysisCardType.ThreeDHeightTrend] = threeDHeightTask.Result.ToSnapshot(),
                    [DataAnalysisCardType.WidthTrend] = widthTask.Result.ToSnapshot(),
                    [DataAnalysisCardType.LengthTrend] = lengthTask.Result.ToSnapshot(),
                    [DataAnalysisCardType.RejectRateTrend] = new TrendRequestResult
                    {
                        IsSupported = true,
                        Subtitle = rejectSubtitle,
                        Buckets = rejectRateTask.Result
                    }.ToSnapshot(),
                    [DataAnalysisCardType.ProductionRateTrend] = new TrendRequestResult
                    {
                        IsSupported = true,
                        Subtitle = rateSubtitle,
                        Buckets = productionRateTask.Result
                    }.ToSnapshot()
                }
            };
        }

        private bool ApplySnapshot(AnalyticsDashboardSnapshot snapshot, bool isCachedPreview)
        {
            if (snapshot == null)
                return false;

            PopulateProductionOverview(snapshot.ProductionOverview ?? new ProductionOverviewSnapshot());
            PopulateDefectDistribution(
                snapshot.Defects ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                snapshot.InspectionStatus,
                snapshot.RecipeName);

            PopulateTrendCard(DataAnalysisCardType.HeightTrend, TrendRequestResult.FromSnapshot(snapshot.Trends, DataAnalysisCardType.HeightTrend));
            PopulateTrendCard(DataAnalysisCardType.ThreeDHeightTrend, TrendRequestResult.FromSnapshot(snapshot.Trends, DataAnalysisCardType.ThreeDHeightTrend));
            PopulateTrendCard(DataAnalysisCardType.WidthTrend, TrendRequestResult.FromSnapshot(snapshot.Trends, DataAnalysisCardType.WidthTrend));
            PopulateTrendCard(DataAnalysisCardType.LengthTrend, TrendRequestResult.FromSnapshot(snapshot.Trends, DataAnalysisCardType.LengthTrend));
            PopulateTrendCard(DataAnalysisCardType.RejectRateTrend, TrendRequestResult.FromSnapshot(snapshot.Trends, DataAnalysisCardType.RejectRateTrend));
            PopulateTrendCard(DataAnalysisCardType.ProductionRateTrend, TrendRequestResult.FromSnapshot(snapshot.Trends, DataAnalysisCardType.ProductionRateTrend));

            StatusMessage = string.Format(
                CultureInfo.InvariantCulture,
                isCachedPreview
                    ? GetMessage("Sub_entry_DataAnalysisStatusCached", "Cached snapshot: {0:dd/MM/yyyy HH:mm}")
                    : GetMessage("Sub_entry_DataAnalysisStatusReady", "Updated: {0:dd/MM/yyyy HH:mm}"),
                snapshot.CreatedAt);

            return true;
        }

        private void PopulateProductionOverview(ProductionOverviewSnapshot snapshot)
        {
            ProductionBars.Clear();

            int total = Math.Max(1, snapshot.Total);
            int maxValue = Math.Max(1, new[] { snapshot.Total, snapshot.Good, snapshot.NoGood, snapshot.Unclassified }.Max());
            ProductionBars.Add(CreateBarItem(TotalLabel, snapshot.Total, 1.0, TotalBarColorText, maxValue));
            ProductionBars.Add(CreateBarItem(GoodLabel, snapshot.Good, snapshot.Good / (double)total, GoodBarColorText, maxValue));
            ProductionBars.Add(CreateBarItem(NoGoodLabel, snapshot.NoGood, snapshot.NoGood / (double)total, NoGoodBarColorText, maxValue));
            ProductionBars.Add(CreateBarItem(UnclassifiedLabel, snapshot.Unclassified, snapshot.Unclassified / (double)total, "#C58A20", maxValue));
        }

        private DataAnalysisBarItem CreateBarItem(string label, int value, double percentage, string colorHex, int maxValue)
        {
            return new DataAnalysisBarItem
            {
                Label = label,
                Value = value,
                Percentage = percentage,
                Width = Math.Max(16, (value / (double)maxValue) * BarMaxWidth),
                Fill = CreateBrush(colorHex, "#1F6FA7")
            };
        }

        private void PopulateDefectDistribution(
            Dictionary<string, int> defects,
            RecipeParameters.InspectionStatus status,
            string recipeName)
        {
            DefectItems.Clear();

            var filtered = BuildFilteredDefectList(defects, status, recipeName)
                .Where(item => item.Value > 0)
                .ToList();

            double total = filtered.Sum(item => item.Value);
            if (total <= 0)
            {
                OnPropertyChanged(nameof(HasDefectData));
                return;
            }

            double startAngle = -90;
            var palette = new[]
            {
                "#1F6FA7", "#3CB371", "#E67E22", "#D35454", "#8E44AD", "#16A085",
                "#C0392B", "#2E86C1", "#F1C40F", "#7F8C8D", "#D81B60", "#5D6D7E"
            };

            for (int i = 0; i < filtered.Count; i++)
            {
                var entry = filtered[i];
                double sweepAngle = (entry.Value / total) * 360.0;
                var geometry = CreatePieSlice(startAngle, sweepAngle);
                startAngle += sweepAngle;

                DefectItems.Add(new DataAnalysisDefectItem
                {
                    Label = entry.Label,
                    Value = entry.Value,
                    Percentage = entry.Value / total,
                    SliceGeometry = geometry,
                    Fill = CreateBrush(palette[i % palette.Length], "#1F6FA7")
                });
            }

            OnPropertyChanged(nameof(HasDefectData));
        }

        private List<DataAnalysisDefectItem> BuildFilteredDefectList(
            Dictionary<string, int> defects,
            RecipeParameters.InspectionStatus status,
            string recipeName)
        {
            var items = new List<(string key, string label, bool enabled)>
            {
                ("Logo", GetMessage("Sub_entry_DataAnalysisDefectLogo", "Logo"), status?.logo ?? true),
                ("PrintCentering", GetMessage("Sub_entry_DataAnalysisDefectPrintCentering", "Print centering"), status?.Print_centering ?? true),
                ("OpenFlaps", GetMessage("Sub_entry_DataAnalysisDefectOpenFlaps", "Open flaps"), status?.OpenFlaps ?? true),
                ("SurfaceCheck", GetMessage("Sub_entry_DataAnalysisDefectSurface", "Surface"), status?.SurfaceCheck ?? true),
                ("Height", GetMessage("Sub_entry_DataAnalysisDefectHeight", "Height"), status?.Height ?? true),
                ("SideSealing", GetMessage("Sub_entry_DataAnalysisDefectSideSealing", "Side sealing"), status?.Side_sealing ?? true),
                ("ShapeTop", GetMessage("Sub_entry_DataAnalysisDefectShapeTop", "Shape TOP"), status?.ShapeTop ?? true),
                ("ShapeSide", GetMessage("Sub_entry_DataAnalysisDefectShapeSide", "Shape SIDE"), status?.ShapeSide ?? true),
                ("FrontTraceability", GetMessage("Sub_entry_DataAnalysisDefectTraceability", "Traceability"), status?.FrontTraceability ?? true),
                ("ThreeDHeight", GetMessage("Sub_entry_DataAnalysisDefect3DHeight", "3D height"), status?.ThreeDHeight ?? true),
                ("ThreeDWidth", GetMessage("Sub_entry_DataAnalysisDefect3DWidth", "3D width"), status?.ThreeDWidth ?? true),
                ("ThreeDLength", GetMessage("Sub_entry_DataAnalysisDefect3DLength", "3D length"), status?.ThreeDLength ?? true),
                ("BottomSealing", GetMessage("Sub_entry_DataAnalysisDefectBottomSealing", "Bottom sealing"), status?.BottomSealing ?? true),
                ("TrappedPaper", GetMessage("Sub_entry_DataAnalysisDefectTrappedPaper", "Trapped paper"), status?.TrappedPaper ?? true)
            };

            string runtimeRecipe = MainWindow.ConfigRecipeParam?.Config?.general_Info?.RecipeName
                ?? MainWindow.configManager?.Config?.Configuration?.LastRecipe;
            Dictionary<string, bool> runtimeFeatures = AreRecipeNamesEquivalent(recipeName, runtimeRecipe)
                ? ServiceLocator.InspectionConfigService?.GetEnabledFeatures()
                : null;

            return items
                .Where(item => item.enabled &&
                    (runtimeFeatures == null ||
                     !runtimeFeatures.TryGetValue(item.key, out bool runtimeEnabled) ||
                     runtimeEnabled))
                .Select(item => new DataAnalysisDefectItem
                {
                    Label = item.label,
                    Value = defects.TryGetValue(item.key, out int value) ? value : 0
                })
                .ToList();
        }

        private void PopulateTrendCard(DataAnalysisCardType cardType, TrendRequestResult result)
        {
            if (!_trendCardMap.TryGetValue(cardType, out var card))
                return;

            card.IsVisible = IsCardVisible(cardType) && result.IsSupported;
            card.Subtitle = result.Subtitle;
            card.ValueColumn = result.ResolvedColumn;
            card.XAxisLabels.Clear();
            card.YAxisLabels.Clear();
            card.NominalPolylinePoints = string.Empty;
            card.LowerTolerancePolylinePoints = string.Empty;
            card.UpperTolerancePolylinePoints = string.Empty;
            card.HasReferenceBand = false;
            card.ToleranceBandTop = 0;
            card.ToleranceBandHeight = 0;

            if (!result.IsSupported)
            {
                card.SummaryText = string.IsNullOrWhiteSpace(result.SummaryText)
                    ? GetMessage("Sub_entry_DataAnalysisTrendDisabled", "Inspection not enabled for selected recipe.")
                    : result.SummaryText;
                card.AveragePolylinePoints = string.Empty;
                card.MinimumPolylinePoints = string.Empty;
                card.MaximumPolylinePoints = string.Empty;
                card.AveragePathData = null;
                card.MinimumPathData = null;
                card.MaximumPathData = null;
                card.AverageAreaPathData = null;
                return;
            }

            if (result.Buckets == null || result.Buckets.Count == 0)
            {
                card.SummaryText = EmptyDataLabel;
                card.AveragePolylinePoints = string.Empty;
                card.MinimumPolylinePoints = string.Empty;
                card.MaximumPolylinePoints = string.Empty;
                card.AveragePathData = null;
                card.MinimumPathData = null;
                card.MaximumPathData = null;
                card.AverageAreaPathData = null;
                return;
            }

            double dataMin = result.Buckets.Min(b => b.Minimum);
            double dataMax = result.Buckets.Max(b => b.Maximum);
            double referenceMin = result.LowerToleranceValue ?? dataMin;
            double referenceMax = result.UpperToleranceValue ?? dataMax;
            double plotMin = Math.Min(dataMin, referenceMin);
            double plotMax = Math.Max(dataMax, referenceMax);
            double padding = Math.Max((plotMax - plotMin) * 0.10, 0.5);
            plotMin -= padding;
            plotMax += padding;
            if (Math.Abs(plotMax - plotMin) < 0.0001)
            {
                plotMax = plotMin + 1;
            }

            card.AveragePolylinePoints = string.Empty;
            card.MinimumPolylinePoints = string.Empty;
            card.MaximumPolylinePoints = string.Empty;
            card.AveragePathData = BuildSmoothPathGeometry(result.Buckets, b => b.Average, plotMin, plotMax);
            card.MinimumPathData = BuildSmoothPathGeometry(result.Buckets, b => b.Minimum, plotMin, plotMax);
            card.MaximumPathData = BuildSmoothPathGeometry(result.Buckets, b => b.Maximum, plotMin, plotMax);
            card.AverageAreaPathData = BuildSmoothAreaGeometry(result.Buckets, b => b.Average, plotMin, plotMax);
            BuildTrendAxisLabels(card, result.Buckets, plotMin, plotMax);

            if (result.NominalValue.HasValue && result.LowerToleranceValue.HasValue && result.UpperToleranceValue.HasValue)
            {
                card.NominalPolylinePoints = BuildHorizontalLine(result.NominalValue.Value, plotMin, plotMax);
                card.LowerTolerancePolylinePoints = BuildHorizontalLine(result.LowerToleranceValue.Value, plotMin, plotMax);
                card.UpperTolerancePolylinePoints = BuildHorizontalLine(result.UpperToleranceValue.Value, plotMin, plotMax);
                card.ToleranceBandTop = Math.Min(
                    ScaleY(result.UpperToleranceValue.Value, plotMin, plotMax),
                    ScaleY(result.LowerToleranceValue.Value, plotMin, plotMax));
                card.ToleranceBandHeight = Math.Abs(
                    ScaleY(result.LowerToleranceValue.Value, plotMin, plotMax) -
                    ScaleY(result.UpperToleranceValue.Value, plotMin, plotMax));
                card.HasReferenceBand = true;
            }

            double avg = result.Buckets.Average(b => b.Average);
            string unit = card.ValueUnit ?? string.Empty;
            int totalSamples = result.Buckets.Sum(b => b.SampleCount);

            if (!string.IsNullOrEmpty(unit))
            {
                card.SummaryText = string.Format(
                    CultureInfo.InvariantCulture,
                    "Min {0:0.##}{4}  |  Avg {1:0.##}{4}  |  Max {2:0.##}{4}  |  N={3}",
                    dataMin, avg, dataMax, totalSamples, unit);
            }
            else
            {
                card.SummaryText = string.Format(
                    CultureInfo.InvariantCulture,
                    "Column {4} | Min {0:0.###} | Avg {1:0.###} | Max {2:0.###} | Samples {3}{5}",
                    dataMin, avg, dataMax, totalSamples,
                    result.ResolvedColumn ?? "--",
                    FormatReferenceSummary(result));
            }
        }

        private static string BuildSmoothPathString(IReadOnlyList<DataAnalysisTrendBucket> buckets, Func<DataAnalysisTrendBucket, double> selector, double min, double max)
        {
            if (buckets == null || buckets.Count == 0)
                return string.Empty;

            if (buckets.Count == 1)
            {
                double y = ScaleY(selector(buckets[0]), min, max);
                return string.Format(CultureInfo.InvariantCulture, "M 0,{0:0.##} L {1:0.##},{0:0.##}", y, TrendWidth);
            }

            double stepX = TrendWidth / (buckets.Count - 1.0);
            var pts = buckets.Select((b, i) => new { X = i * stepX, Y = ScaleY(selector(b), min, max) }).ToArray();

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "M {0:0.##},{1:0.##}", pts[0].X, pts[0].Y);

            for (int i = 0; i < pts.Length - 1; i++)
            {
                var prev = pts[Math.Max(0, i - 1)];
                var curr = pts[i];
                var next = pts[i + 1];
                var next2 = pts[Math.Min(pts.Length - 1, i + 2)];

                double c1x = curr.X + (next.X - prev.X) / 6.0;
                double c1y = Math.Max(0, Math.Min(TrendHeight, curr.Y + (next.Y - prev.Y) / 6.0));
                double c2x = next.X - (next2.X - curr.X) / 6.0;
                double c2y = Math.Max(0, Math.Min(TrendHeight, next.Y - (next2.Y - curr.Y) / 6.0));

                sb.AppendFormat(CultureInfo.InvariantCulture, " C {0:0.##},{1:0.##} {2:0.##},{3:0.##} {4:0.##},{5:0.##}",
                    c1x, c1y, c2x, c2y, next.X, next.Y);
            }

            return sb.ToString();
        }

        private static Geometry BuildSmoothPathGeometry(IReadOnlyList<DataAnalysisTrendBucket> buckets, Func<DataAnalysisTrendBucket, double> selector, double min, double max)
        {
            string pathData = BuildSmoothPathString(buckets, selector, min, max);
            if (string.IsNullOrEmpty(pathData))
                return Geometry.Empty;

            var geo = Geometry.Parse(pathData);
            geo.Freeze();
            return geo;
        }

        private static Geometry BuildSmoothAreaGeometry(IReadOnlyList<DataAnalysisTrendBucket> buckets, Func<DataAnalysisTrendBucket, double> selector, double min, double max)
        {
            string linePath = BuildSmoothPathString(buckets, selector, min, max);
            if (string.IsNullOrEmpty(linePath))
                return Geometry.Empty;

            double lastX = buckets.Count == 1 ? 0 : TrendWidth;
            string closedPath = string.Format(CultureInfo.InvariantCulture,
                "{0} L {1:0.##},{2:0.##} L 0,{2:0.##} Z", linePath, lastX, TrendHeight);

            var geo = Geometry.Parse(closedPath);
            geo.Freeze();
            return geo;
        }

        private static string BuildHorizontalLine(double value, double min, double max)
        {
            double y = ScaleY(value, min, max);
            return string.Format(CultureInfo.InvariantCulture, "0,{0:0.##} {1:0.##},{0:0.##}", y, TrendWidth);
        }

        private static string FormatReferenceSummary(TrendRequestResult result)
        {
            if (!result.NominalValue.HasValue || !result.LowerToleranceValue.HasValue || !result.UpperToleranceValue.HasValue)
                return string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                " | Nom {0:0.###} | Tol [{1:0.###} .. {2:0.###}]",
                result.NominalValue.Value,
                result.LowerToleranceValue.Value,
                result.UpperToleranceValue.Value);
        }

        private static void BuildTrendAxisLabels(
            DataAnalysisTrendCard card,
            IReadOnlyList<DataAnalysisTrendBucket> buckets,
            double min,
            double max)
        {
            if (card == null)
                return;

            card.XAxisLabels.Clear();
            card.YAxisLabels.Clear();

            for (int i = 0; i < 5; i++)
            {
                double ratio = i / 4.0;
                double y = ratio * TrendHeight;
                double value = max - ((max - min) * ratio);
                card.YAxisLabels.Add(new DataAnalysisAxisLabel
                {
                    Text = value.ToString("0.##", CultureInfo.InvariantCulture),
                    X = 0,
                    Y = y - 10
                });
            }

            if (buckets == null || buckets.Count == 0)
                return;

            bool spansMultipleDays = buckets.Count > 1 &&
                buckets[buckets.Count - 1].Timestamp.Date != buckets[0].Timestamp.Date;

            if (buckets.Count == 1)
            {
                card.XAxisLabels.Add(new DataAnalysisAxisLabel
                {
                    Text = FormatXAxisLabel(buckets[0].Timestamp, spansMultipleDays),
                    X = 0,
                    Y = 0
                });
                return;
            }

            for (int i = 0; i < 5; i++)
            {
                int index = (int)Math.Round(((buckets.Count - 1) * i) / 4.0, MidpointRounding.AwayFromZero);
                index = Math.Max(0, Math.Min(buckets.Count - 1, index));
                double x = (index / (double)(buckets.Count - 1)) * TrendWidth;
                card.XAxisLabels.Add(new DataAnalysisAxisLabel
                {
                    Text = FormatXAxisLabel(buckets[index].Timestamp, spansMultipleDays),
                    X = Math.Max(0, Math.Min(TrendWidth - TrendAxisLabelWidth, x - (TrendAxisLabelWidth / 2.0))),
                    Y = 0
                });
            }
        }

        private static double ScaleY(double value, double min, double max)
        {
            double ratio = (value - min) / (max - min);
            return TrendHeight - (ratio * TrendHeight);
        }

        private static bool TryParseTime(string input, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (DateTime.TryParseExact(input.Trim(), new[] { "HH:mm", "H:mm", "hh:mm" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                result = dt.TimeOfDay;
                return true;
            }

            return false;
        }

        private static string FormatXAxisLabel(DateTime timestamp, bool includeDate)
        {
            return includeDate
                ? timestamp.ToString("HH:mm", CultureInfo.InvariantCulture) + "\n" + timestamp.ToString("d/M", CultureInfo.InvariantCulture)
                : timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private async Task<TrendRequestResult> ResolveTrendTaskAsync(
            DataAnalysisCardType cardType,
            DateTime start,
            DateTime end,
            string recipeName,
            RecipeContextResult recipeContext)
        {
            if (!_measurementDefinitions.TryGetValue(cardType, out MeasurementTrendDefinition definition))
            {
                return new TrendRequestResult
                {
                    IsSupported = false,
                    SummaryText = GetMessage("Sub_entry_DataAnalysisTrendNotConfigured", "Measurement not configured.")
                };
            }

            if (!definition.IsInspectionEnabled(recipeContext?.InspectionStatus))
            {
                return new TrendRequestResult
                {
                    IsSupported = false,
                    Subtitle = definition.Subtitle,
                    SummaryText = GetMessage("Sub_entry_DataAnalysisTrendDisabled", "Inspection not enabled for selected recipe.")
                };
            }

            string resolvedColumn = await _repository.ResolveExistingColumnAsync("tblgenerale", definition.Candidates).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(resolvedColumn))
            {
                return new TrendRequestResult
                {
                    IsSupported = false,
                    Subtitle = definition.Subtitle,
                    SummaryText = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: {1}",
                        GetMessage("Sub_entry_DataAnalysisTrendColumnMissing", "Measurement column not found"),
                        string.Join(" / ", definition.Candidates))
                };
            }

            var buckets = await _repository.GetMeasurementTrendAsync(start, end, recipeName, resolvedColumn).ConfigureAwait(true);
            var referenceRange = definition.ReferenceResolver?.Invoke(recipeContext?.RecipeData);
            return new TrendRequestResult
            {
                IsSupported = true,
                Subtitle = definition.Subtitle,
                ResolvedColumn = resolvedColumn,
                Buckets = buckets,
                NominalValue = referenceRange?.NominalValue,
                LowerToleranceValue = referenceRange?.LowerToleranceValue,
                UpperToleranceValue = referenceRange?.UpperToleranceValue
            };
        }

        private (DateTime start, DateTime end) ResolveRange()
        {
            var start = _startDate;
            var end = _endDate;

            if (end < start)
            {
                end = start;
                _endDate = start;
                OnPropertyChanged(nameof(EndDate));
                OnPropertyChanged(nameof(EndTimeText));
            }

            return (start, end);
        }

        private async Task<RecipeContextResult> LoadRecipeContextAsync(string recipeName)
        {
            string effectiveRecipeName = recipeName;
            if (string.IsNullOrWhiteSpace(effectiveRecipeName))
            {
                return null;
            }

            try
            {
                string folder = MainWindow.configManager?.Config?.Configuration?.Recipe_Folder;
                if (string.IsNullOrWhiteSpace(folder))
                    return CreateRuntimeRecipeContext(effectiveRecipeName);

                string recipePath = ResolveRecipeFilePath(folder, effectiveRecipeName);
                if (string.IsNullOrWhiteSpace(recipePath))
                {
                    MainWindow.logger?.Warn($"Analytics recipe resolver did not find a valid XML for '{effectiveRecipeName}'.");
                    return CreateRuntimeRecipeContext(effectiveRecipeName);
                }

                var manager = new AsyncRecipeParam(recipePath);
                await manager.EnsureLoadedAsync().ConfigureAwait(true);
                return new RecipeContextResult
                {
                    ResolvedRecipeName = effectiveRecipeName,
                    ResolvedRecipePath = recipePath,
                    RecipeData = manager.Config,
                    InspectionStatus = manager.Config?.inspectionStatus
                        ?? RecipeParameters.InspectionStatus.FromEjectionStatus(manager.Config?.ejectionStatus)
                };
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Unable to load recipe inspection status for analytics '{effectiveRecipeName}': {ex.Message}");
                return CreateRuntimeRecipeContext(effectiveRecipeName);
            }
        }

        private string ResolveEffectiveRecipeName()
        {
            if (SelectedRecipe == null)
            {
                return MainWindow.configManager?.Config?.Configuration?.LastRecipe;
            }

            if (string.Equals(SelectedRecipe.Value, ActiveRecipeFilterValue, StringComparison.OrdinalIgnoreCase))
            {
                return MainWindow.configManager?.Config?.Configuration?.LastRecipe;
            }

            return SelectedRecipe.Value;
        }

        private static string ResolveRecipeFilePath(string folder, string recipeName)
        {
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(recipeName))
                return null;

            string trimmed = recipeName.Trim();
            string noExt = Path.GetFileNameWithoutExtension(trimmed);
            var candidates = new[]
            {
                Path.Combine(folder, trimmed + ".xml"),
                Path.Combine(folder, noExt + ".xml")
            };

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IsValidRecipeXmlFile(candidate))
                {
                    return candidate;
                }
            }

            try
            {
                return Directory.GetFiles(folder, "*.xml", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path =>
                        IsValidRecipeXmlFile(path) &&
                        (string.Equals(Path.GetFileNameWithoutExtension(path), noExt, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Path.GetFileName(path), noExt + ".xml", StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return null;
            }
        }

        private static bool IsValidRecipeXmlFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path) ||
                !string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Ignore,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                }))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == System.Xml.XmlNodeType.Element)
                        {
                            return string.Equals(reader.Name, "Recipedata", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Analytics recipe resolver skipped invalid XML '{path}': {ex.Message}");
            }

            return false;
        }

        private static RecipeContextResult CreateRuntimeRecipeContext(string requestedRecipeName)
        {
            string currentRuntimeRecipe = MainWindow.ConfigRecipeParam?.Config?.general_Info?.RecipeName
                ?? MainWindow.configManager?.Config?.Configuration?.LastRecipe;

            if (!string.IsNullOrWhiteSpace(requestedRecipeName) &&
                !AreRecipeNamesEquivalent(requestedRecipeName, currentRuntimeRecipe))
            {
                return null;
            }

            var runtimeRecipe = MainWindow.ConfigRecipeParam?.Config;
            if (runtimeRecipe == null)
                return null;

            return new RecipeContextResult
            {
                ResolvedRecipeName = currentRuntimeRecipe,
                ResolvedRecipePath = null,
                RecipeData = runtimeRecipe,
                InspectionStatus = runtimeRecipe.inspectionStatus
                    ?? RecipeParameters.InspectionStatus.FromEjectionStatus(runtimeRecipe.ejectionStatus)
            };
        }

        private static bool AreRecipeNamesEquivalent(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            string normalizedLeft = Path.GetFileNameWithoutExtension(left.Trim());
            string normalizedRight = Path.GetFileNameWithoutExtension(right.Trim());
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static TrendReferenceRange CreateThreeDReference(double? nominal, double? tolerance)
        {
            double nominalValue = nominal ?? 0;
            double toleranceValue = tolerance ?? 0;
            if (nominalValue <= 0 && toleranceValue <= 0)
                return null;

            return new TrendReferenceRange
            {
                NominalValue = nominalValue,
                LowerToleranceValue = nominalValue - toleranceValue,
                UpperToleranceValue = nominalValue + toleranceValue
            };
        }

        private void LoadDashboardConfig()
        {
            var settings = MainWindow.configManager?.Config?.AnalyticsDashboard ?? new AnalyticsDashboardSettings();
            TotalBarColorText = settings.TotalBarColor;
            GoodBarColorText = settings.GoodBarColor;
            NoGoodBarColorText = settings.NoGoodBarColor;

            foreach (var pair in _trendCardMap)
            {
                pair.Value.IsVisible = IsCardVisible(pair.Key);
            }
        }

        private async Task SaveDashboardConfigAsync()
        {
            if (!CanEditDashboard)
                return;

            var manager = MainWindow.configManager;
            var settings = manager?.Config?.AnalyticsDashboard;
            if (settings == null)
            {
                StatusMessage = GetMessage("Sub_entry_DataAnalysisSaveFailed", "Dashboard save failed.");
                return;
            }

            settings.TotalBarColor = NormalizeColorHex(TotalBarColorText, "#1F6FA7");
            settings.GoodBarColor = NormalizeColorHex(GoodBarColorText, "#35A26B");
            settings.NoGoodBarColor = NormalizeColorHex(NoGoodBarColorText, "#D35454");
            settings.RefreshMinutes = GetRefreshMinutes();
            settings.Cards = GetPersistedCards();

            try
            {
                await manager.SaveConfigAsync().ConfigureAwait(true);
                _refreshTimer.Interval = TimeSpan.FromMinutes(GetRefreshMinutes());
                LoadDashboardConfig();
                RebuildAvailableCards();
                PopulateProductionOverview(new ProductionOverviewSnapshot
                {
                    Total = ProductionBars.ElementAtOrDefault(0)?.Value ?? 0,
                    Good = ProductionBars.ElementAtOrDefault(1)?.Value ?? 0,
                    NoGood = ProductionBars.ElementAtOrDefault(2)?.Value ?? 0,
                    Unclassified = ProductionBars.ElementAtOrDefault(3)?.Value ?? 0
                });
                StatusMessage = GetMessage("Sub_entry_DataAnalysisSaved", "Dashboard configuration saved.");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Unable to save analytics dashboard config: {ex.Message}");
                StatusMessage = GetMessage("Sub_entry_DataAnalysisSaveFailed", "Dashboard save failed.");
            }
            finally
            {
                _isEditModeActive = false;
                NotifyEditModeChanged();
            }
        }

        private void NotifyEditModeChanged()
        {
            OnPropertyChanged(nameof(CanEditDashboard));
            OnPropertyChanged(nameof(EditOrSaveLabel));
            CommandManager.InvalidateRequerySuggested();
        }

        private void AddSelectedCard()
        {
            if (!CanEditDashboard || SelectedAvailableCard == null)
                return;

            SetCardVisibility(SelectedAvailableCard.CardType, true);
            RebuildAvailableCards();
            CommandManager.InvalidateRequerySuggested();
        }

        private void RemoveCard(object parameter)
        {
            if (!CanEditDashboard)
                return;

            if (parameter is DataAnalysisCardType cardType)
            {
                SetCardVisibility(cardType, false);
                RebuildAvailableCards();
                CommandManager.InvalidateRequerySuggested();
            }
            else if (parameter is string raw && Enum.TryParse(raw, out DataAnalysisCardType parsed))
            {
                SetCardVisibility(parsed, false);
                RebuildAvailableCards();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool IsCardVisible(DataAnalysisCardType cardType)
        {
            var settings = MainWindow.configManager?.Config?.AnalyticsDashboard;
            if (settings?.Cards == null || settings.Cards.Count == 0)
            {
                return true;
            }

            return settings.Cards.FirstOrDefault(card => string.Equals(card.CardType, cardType.ToString(), StringComparison.OrdinalIgnoreCase))?.IsVisible ?? false;
        }

        private void SetCardVisibility(DataAnalysisCardType cardType, bool visible)
        {
            var settings = MainWindow.configManager?.Config?.AnalyticsDashboard;
            if (settings == null)
                return;

            var existing = settings.Cards.FirstOrDefault(card => string.Equals(card.CardType, cardType.ToString(), StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new AnalyticsCardConfig
                {
                    CardType = cardType.ToString(),
                    IsVisible = visible
                };
                settings.Cards.Add(existing);
            }
            else
            {
                existing.IsVisible = visible;
            }

            if (_trendCardMap.TryGetValue(cardType, out var trend))
            {
                trend.IsVisible = visible;
            }

            OnPropertyChanged(nameof(IsProductionOverviewVisible));
            OnPropertyChanged(nameof(IsDefectPieVisible));
        }

        public bool IsProductionOverviewVisible => IsCardVisible(DataAnalysisCardType.ProductionOverview);
        public bool IsDefectPieVisible => IsCardVisible(DataAnalysisCardType.DefectPie);

        private void RebuildAvailableCards()
        {
            AvailableCards.Clear();
            foreach (DataAnalysisCardType cardType in Enum.GetValues(typeof(DataAnalysisCardType)))
            {
                if (IsCardVisible(cardType))
                    continue;

                AvailableCards.Add(new DataAnalysisAvailableCardOption
                {
                    CardType = cardType,
                    Label = _cardTitles.TryGetValue(cardType, out string title) ? title : cardType.ToString()
                });
            }

            SelectedAvailableCard = AvailableCards.FirstOrDefault();
            OnPropertyChanged(nameof(IsProductionOverviewVisible));
            OnPropertyChanged(nameof(IsDefectPieVisible));
        }

        private List<AnalyticsCardConfig> GetPersistedCards()
        {
            return Enum.GetValues(typeof(DataAnalysisCardType))
                .Cast<DataAnalysisCardType>()
                .Select(cardType => new AnalyticsCardConfig
                {
                    CardType = cardType.ToString(),
                    IsVisible = IsCardVisible(cardType)
                })
                .ToList();
        }

        private static int GetRefreshMinutes()
        {
            return Math.Max(1, MainWindow.configManager?.Config?.AnalyticsDashboard?.RefreshMinutes ?? 5);
        }

        private static string NormalizeColorHex(string candidate, string fallback)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(candidate);
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            catch
            {
                return fallback;
            }
        }

        private static SolidColorBrush CreateBrush(string candidate, string fallback)
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(candidate));
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            }
        }

        private static Geometry CreatePieSlice(double startAngle, double sweepAngle)
        {
            if (sweepAngle <= 0)
                return Geometry.Empty;

            var startPoint = PointOnCircle(startAngle);
            var endPoint = PointOnCircle(startAngle + sweepAngle);
            bool isLargeArc = sweepAngle > 180;

            var figure = new PathFigure
            {
                StartPoint = new System.Windows.Point(PieCenter, PieCenter),
                IsClosed = true,
                IsFilled = true
            };
            figure.Segments.Add(new LineSegment(startPoint, true));
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new System.Windows.Size(PieRadius, PieRadius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });
            figure.Segments.Add(new LineSegment(new System.Windows.Point(PieCenter, PieCenter), true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }

        private static System.Windows.Point PointOnCircle(double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            double x = PieCenter + (Math.Cos(radians) * PieRadius);
            double y = PieCenter + (Math.Sin(radians) * PieRadius);
            return new System.Windows.Point(x, y);
        }

        private static string GetMessage(string key, string fallback)
        {
            return ServerMessagePersonalize.GetMessageOrDefault(key, fallback);
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= RefreshTimer_Tick;
            UserSession.OnRoleChanged -= UserSession_OnRoleChanged;
        }

        private class TrendRequestResult
        {
            public bool IsSupported { get; set; }
            public string Subtitle { get; set; }
            public string ResolvedColumn { get; set; }
            public string SummaryText { get; set; }
            public IReadOnlyList<DataAnalysisTrendBucket> Buckets { get; set; }
            public double? NominalValue { get; set; }
            public double? LowerToleranceValue { get; set; }
            public double? UpperToleranceValue { get; set; }

            public AnalyticsTrendSnapshot ToSnapshot()
            {
                return new AnalyticsTrendSnapshot
                {
                    IsSupported = IsSupported,
                    Subtitle = Subtitle,
                    ResolvedColumn = ResolvedColumn,
                    SummaryText = SummaryText,
                    Buckets = Buckets?.ToList() ?? new List<DataAnalysisTrendBucket>(),
                    NominalValue = NominalValue,
                    LowerToleranceValue = LowerToleranceValue,
                    UpperToleranceValue = UpperToleranceValue
                };
            }

            public static TrendRequestResult FromSnapshot(
                Dictionary<DataAnalysisCardType, AnalyticsTrendSnapshot> snapshots,
                DataAnalysisCardType cardType)
            {
                if (snapshots == null || !snapshots.TryGetValue(cardType, out var snapshot) || snapshot == null)
                {
                    return new TrendRequestResult
                    {
                        IsSupported = false,
                        SummaryText = DataAnalysisViewModel.GetMessage("Sub_entry_DataAnalysisTrendNotConfigured", "Measurement not configured.")
                    };
                }

                return new TrendRequestResult
                {
                    IsSupported = snapshot.IsSupported,
                    Subtitle = snapshot.Subtitle,
                    ResolvedColumn = snapshot.ResolvedColumn,
                    SummaryText = snapshot.SummaryText,
                    Buckets = snapshot.Buckets,
                    NominalValue = snapshot.NominalValue,
                    LowerToleranceValue = snapshot.LowerToleranceValue,
                    UpperToleranceValue = snapshot.UpperToleranceValue
                };
            }
        }

        private class MeasurementTrendDefinition
        {
            public DataAnalysisCardType CardType { get; set; }
            public string[] Candidates { get; set; }
            public Func<RecipeParameters.InspectionStatus, bool> IsInspectionEnabled { get; set; }
            public string Subtitle { get; set; }
            public Func<RecipeParameters.RecipeData, TrendReferenceRange> ReferenceResolver { get; set; }
        }

        private class RecipeContextResult
        {
            public string ResolvedRecipeName { get; set; }
            public string ResolvedRecipePath { get; set; }
            public RecipeParameters.RecipeData RecipeData { get; set; }
            public RecipeParameters.InspectionStatus InspectionStatus { get; set; }
        }

        private class TrendReferenceRange
        {
            public double? NominalValue { get; set; }
            public double? LowerToleranceValue { get; set; }
            public double? UpperToleranceValue { get; set; }
        }
    }
}
