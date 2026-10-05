using MySql.Data.MySqlClient;
using QtisVisionPanel.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QtisVisionPanel.Database
{
    public class ProductionOverviewSnapshot
    {
        public int Total { get; set; }
        public int Good { get; set; }
        public int NoGood { get; set; }
        public int Unclassified { get; set; }
    }

    public class ProductionAnalyticsRepository
    {
        private const string RecipeFilterClause = @"
  AND (
        @Recipe IS NULL OR
        TRIM(Ricetta) = TRIM(@Recipe) OR
        TRIM(Ricetta) = TRIM(@RecipeNoExt) OR
        TRIM(Ricetta) = TRIM(@RecipeWithXml)
      )";

        private readonly ConcurrentDictionary<string, HashSet<string>> _tableColumnsCache =
            new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public async Task<IReadOnlyList<string>> GetRecipesInRangeAsync(DateTime start, DateTime end)
        {
            var items = new List<string>();
            const string query = @"
SELECT DISTINCT Ricetta
FROM tblgenerale
WHERE DataeOra >= @Start
  AND DataeOra <= @End
  AND Ricetta IS NOT NULL
  AND Ricetta <> ''
ORDER BY Ricetta;";

            await ExecuteReaderAsync(query, command =>
            {
                command.Parameters.AddWithValue("@Start", start);
                command.Parameters.AddWithValue("@End", end);
            },
            reader =>
            {
                items.Add(reader.GetString("Ricetta"));
            }).ConfigureAwait(false);

            return items;
        }

        public async Task<ProductionOverviewSnapshot> GetProductionOverviewAsync(DateTime start, DateTime end, string recipeName)
        {
            var snapshot = new ProductionOverviewSnapshot();
            const string query = @"
SELECT 
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN COALESCE(Esito_Classificazione, 1) = 1 THEN 1 ELSE 0 END) AS GoodCount,
    SUM(CASE WHEN Esito_Classificazione = 0 THEN 1 ELSE 0 END) AS NoGoodCount,
    SUM(CASE WHEN Esito_Classificazione = 3 THEN 1 ELSE 0 END) AS UnclassifiedCount
FROM tblgenerale
WHERE DataeOra >= @Start
  AND DataeOra <= @End
  " + RecipeFilterClause + @";";

            await ExecuteReaderAsync(query, command =>
            {
                command.Parameters.AddWithValue("@Start", start);
                command.Parameters.AddWithValue("@End", end);
                AddRecipeParameters(command, recipeName);
            },
            reader =>
            {
                snapshot.Total = reader["TotalCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["TotalCount"], CultureInfo.InvariantCulture);
                snapshot.Good = reader["GoodCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["GoodCount"], CultureInfo.InvariantCulture);
                snapshot.NoGood = reader["NoGoodCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["NoGoodCount"], CultureInfo.InvariantCulture);
                snapshot.Unclassified = reader["UnclassifiedCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["UnclassifiedCount"], CultureInfo.InvariantCulture);
                if (snapshot.Good <= 0 && snapshot.NoGood <= 0 && snapshot.Unclassified <= 0 && snapshot.Total > 0)
                {
                    snapshot.Good = Math.Max(0, snapshot.Total - snapshot.NoGood);
                }
            }).ConfigureAwait(false);

            return snapshot;
        }

        public async Task<Dictionary<string, int>> GetDefectDistributionAsync(DateTime start, DateTime end, string recipeName)
        {
            var items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            const string query = @"
SELECT
    SUM(CASE WHEN NC_Loghi_Immagini = 0 THEN 1 ELSE 0 END) AS Logo,
    SUM(CASE WHEN NC_Centratura_Logo = 0 THEN 1 ELSE 0 END) AS PrintCentering,
    SUM(CASE WHEN NC_Alette_Aperte = 0 THEN 1 ELSE 0 END) AS OpenFlaps,
    SUM(CASE WHEN NC_SurfaceCheck = 0 THEN 1 ELSE 0 END) AS SurfaceCheck,
    SUM(CASE WHEN NC_Heigth = 0 THEN 1 ELSE 0 END) AS Height,
    SUM(CASE WHEN NC_Saldatura_Laterale = 0 THEN 1 ELSE 0 END) AS SideSealing,
    SUM(CASE WHEN NC_ShapeTop = 0 THEN 1 ELSE 0 END) AS ShapeTop,
    SUM(CASE WHEN NC_ShapeBottom = 0 THEN 1 ELSE 0 END) AS ShapeSide,
    SUM(CASE WHEN NC_Traceability = 0 THEN 1 ELSE 0 END) AS FrontTraceability,
    SUM(CASE WHEN NC_ThreeDHeight = 0 THEN 1 ELSE 0 END) AS ThreeDHeight,
    SUM(CASE WHEN NC_ThreeDWidth = 0 THEN 1 ELSE 0 END) AS ThreeDWidth,
    SUM(CASE WHEN NC_ThreeDLength = 0 THEN 1 ELSE 0 END) AS ThreeDLength,
    SUM(CASE WHEN NC_BottomSealing = 0 THEN 1 ELSE 0 END) AS BottomSealing,
    SUM(CASE WHEN NC_TrappedPaper = 0 THEN 1 ELSE 0 END) AS TrappedPaper
FROM tblgenerale
WHERE DataeOra >= @Start
  AND DataeOra <= @End
  " + RecipeFilterClause + @";";

            await ExecuteReaderAsync(query, command =>
            {
                command.Parameters.AddWithValue("@Start", start);
                command.Parameters.AddWithValue("@End", end);
                AddRecipeParameters(command, recipeName);
            },
            reader =>
            {
                foreach (var key in new[]
                {
                    "Logo", "PrintCentering", "OpenFlaps", "SurfaceCheck", "Height", "SideSealing",
                    "ShapeTop", "ShapeSide", "FrontTraceability", "ThreeDHeight", "ThreeDWidth", "ThreeDLength",
                    "BottomSealing", "TrappedPaper"
                })
                {
                    items[key] = reader[key] == DBNull.Value ? 0 : Convert.ToInt32(reader[key], CultureInfo.InvariantCulture);
                }
            }).ConfigureAwait(false);

            return items;
        }

        public async Task<IReadOnlyList<DataAnalysisTrendBucket>> GetRejectRateTrendAsync(DateTime start, DateTime end, string recipeName)
        {
            var items = new List<DataAnalysisTrendBucket>();
            string query = $@"
SELECT
    TIMESTAMP(
        DATE(DataeOra),
        MAKETIME(HOUR(DataeOra), FLOOR(MINUTE(DataeOra) / 15) * 15, 0)
    ) AS BucketTime,
    (SUM(CASE WHEN COALESCE(Esito_Classificazione, 1) = 0 THEN 1 ELSE 0 END) / COUNT(*)) * 100.0 AS RejectRate,
    COUNT(*) AS SampleCount
FROM tblgenerale
WHERE DataeOra >= @Start
  AND DataeOra <= @End
  {RecipeFilterClause}
GROUP BY BucketTime
ORDER BY BucketTime;";

            await ExecuteReaderAsync(query, command =>
            {
                command.Parameters.AddWithValue("@Start", start);
                command.Parameters.AddWithValue("@End", end);
                AddRecipeParameters(command, recipeName);
            },
            reader =>
            {
                double rate = reader["RejectRate"] == DBNull.Value ? 0 : Convert.ToDouble(reader["RejectRate"], CultureInfo.InvariantCulture);
                items.Add(new DataAnalysisTrendBucket
                {
                    Timestamp = reader.GetDateTime("BucketTime"),
                    Average = rate,
                    Minimum = rate,
                    Maximum = rate,
                    SampleCount = reader["SampleCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["SampleCount"], CultureInfo.InvariantCulture)
                });
            }).ConfigureAwait(false);

            return items;
        }

        public async Task<IReadOnlyList<DataAnalysisTrendBucket>> GetProductionRateTrendAsync(DateTime start, DateTime end, string recipeName)
        {
            var items = new List<DataAnalysisTrendBucket>();
            string query = $@"
SELECT
    TIMESTAMP(
        DATE(DataeOra),
        MAKETIME(HOUR(DataeOra), FLOOR(MINUTE(DataeOra) / 15) * 15, 0)
    ) AS BucketTime,
    COUNT(*) AS PieceCount,
    COUNT(*) * 4.0 AS PiecesPerHour
FROM tblgenerale
WHERE DataeOra >= @Start
  AND DataeOra <= @End
  {RecipeFilterClause}
GROUP BY BucketTime
ORDER BY BucketTime;";

            await ExecuteReaderAsync(query, command =>
            {
                command.Parameters.AddWithValue("@Start", start);
                command.Parameters.AddWithValue("@End", end);
                AddRecipeParameters(command, recipeName);
            },
            reader =>
            {
                double rate = reader["PiecesPerHour"] == DBNull.Value ? 0 : Convert.ToDouble(reader["PiecesPerHour"], CultureInfo.InvariantCulture);
                items.Add(new DataAnalysisTrendBucket
                {
                    Timestamp = reader.GetDateTime("BucketTime"),
                    Average = rate,
                    Minimum = rate,
                    Maximum = rate,
                    SampleCount = reader["PieceCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["PieceCount"], CultureInfo.InvariantCulture)
                });
            }).ConfigureAwait(false);

            return items;
        }

        public async Task<IReadOnlyList<DataAnalysisTrendBucket>> GetMeasurementTrendAsync(DateTime start, DateTime end, string recipeName, string valueColumn)
        {
            var items = new List<DataAnalysisTrendBucket>();
            string query = $@"
SELECT
    TIMESTAMP(
        DATE(DataeOra),
        MAKETIME(HOUR(DataeOra), FLOOR(MINUTE(DataeOra) / 15) * 15, 0)
    ) AS BucketTime,
    AVG(`{valueColumn}`) AS AvgMeasureValue,
    MIN(`{valueColumn}`) AS MinMeasureValue,
    MAX(`{valueColumn}`) AS MaxMeasureValue,
    COUNT(*) AS SampleCount
FROM tblgenerale
WHERE DataeOra >= @Start
  AND DataeOra <= @End
  {RecipeFilterClause}
  AND `{valueColumn}` IS NOT NULL
GROUP BY BucketTime
ORDER BY BucketTime;";

            await ExecuteReaderAsync(query, command =>
            {
                command.Parameters.AddWithValue("@Start", start);
                command.Parameters.AddWithValue("@End", end);
                AddRecipeParameters(command, recipeName);
            },
            reader =>
            {
                items.Add(new DataAnalysisTrendBucket
                {
                    Timestamp = reader.GetDateTime("BucketTime"),
                    Average = reader["AvgMeasureValue"] == DBNull.Value ? 0 : Convert.ToDouble(reader["AvgMeasureValue"], CultureInfo.InvariantCulture),
                    Minimum = reader["MinMeasureValue"] == DBNull.Value ? 0 : Convert.ToDouble(reader["MinMeasureValue"], CultureInfo.InvariantCulture),
                    Maximum = reader["MaxMeasureValue"] == DBNull.Value ? 0 : Convert.ToDouble(reader["MaxMeasureValue"], CultureInfo.InvariantCulture),
                    SampleCount = reader["SampleCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["SampleCount"], CultureInfo.InvariantCulture)
                });
            }).ConfigureAwait(false);

            return items;
        }

        public async Task<string> ResolveExistingColumnAsync(string tableName, params string[] candidateColumns)
        {
            if (string.IsNullOrWhiteSpace(tableName) || candidateColumns == null || candidateColumns.Length == 0)
                return null;

            var columns = await GetTableColumnsAsync(tableName).ConfigureAwait(false);
            foreach (string candidate in candidateColumns.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (columns.Contains(candidate))
                    return candidate;
            }

            return null;
        }

        public async Task<HashSet<string>> GetTableColumnsAsync(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_tableColumnsCache.TryGetValue(tableName, out var cached))
                return cached;

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const string query = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @DatabaseName
  AND TABLE_NAME = @TableName;";

            await ExecuteReaderAsync(query, command =>
            {
                command.Parameters.AddWithValue("@DatabaseName", MainWindow.configManager?.Config?.MySqlConnection?.Db);
                command.Parameters.AddWithValue("@TableName", tableName);
            },
            reader =>
            {
                string columnName = reader["COLUMN_NAME"]?.ToString();
                if (!string.IsNullOrWhiteSpace(columnName))
                {
                    columns.Add(columnName);
                }
            }).ConfigureAwait(false);

            _tableColumnsCache[tableName] = columns;
            return columns;
        }

        private static void AddRecipeParameters(MySqlCommand command, string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                command.Parameters.AddWithValue("@Recipe", DBNull.Value);
                command.Parameters.AddWithValue("@RecipeNoExt", DBNull.Value);
                command.Parameters.AddWithValue("@RecipeWithXml", DBNull.Value);
                return;
            }

            string normalized = recipeName.Trim();
            string noExt = Path.GetFileNameWithoutExtension(normalized);
            string withXml = normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : noExt + ".xml";

            command.Parameters.AddWithValue("@Recipe", normalized);
            command.Parameters.AddWithValue("@RecipeNoExt", noExt);
            command.Parameters.AddWithValue("@RecipeWithXml", withXml);
        }

        private async Task ExecuteReaderAsync(string query, Action<MySqlCommand> parameterize, Action<MySqlDataReader> read)
        {
            string connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return;

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync().ConfigureAwait(false);
                    using (var command = new MySqlCommand(query, connection))
                    {
                        parameterize?.Invoke(command);
                        using (var reader = (MySqlDataReader)await command.ExecuteReaderAsync().ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync().ConfigureAwait(false))
                            {
                                read(reader);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"ProductionAnalyticsRepository query failed: {ex.Message}");
            }
        }

        private static string BuildConnectionString()
        {
            var config = MainWindow.configManager?.Config?.MySqlConnection;
            if (config == null)
                return null;

            return $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
        }
    }
}
