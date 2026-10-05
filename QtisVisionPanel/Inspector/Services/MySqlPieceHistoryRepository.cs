using MySql.Data.MySqlClient;
using QtisVisionPanel.Inspector.Abstractions;
using QtisVisionPanel.Inspector.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace QtisVisionPanel.Inspector.Services
{
    public sealed class MySqlPieceHistoryRepository : IPieceHistoryRepository, IDataSourceHealthProvider
    {
        private static readonly Regex IdentifierRegex = new Regex("^[A-Za-z0-9_]+$", RegexOptions.Compiled);
        private static readonly IReadOnlyDictionary<string, Tuple<string, string, string[]>> InspectionMap =
            new Dictionary<string, Tuple<string, string, string[]>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LOGO"] = Tuple.Create("NC_Loghi_Immagini", "Logo", new[] { "logo" }),
                ["PRINT_CENTERING"] = Tuple.Create("NC_Centratura_Logo", "Print Centering", new[] { "print centering", "centring", "centratura" }),
                ["OPEN_FLAPS"] = Tuple.Create("NC_Alette_Aperte", "Open Flaps", new[] { "open flaps", "alette aperte", "flaps" }),
                ["HEIGHT"] = Tuple.Create("NC_Heigth", "Height", new[] { "height", "altezza" }),
                ["SIDE_SEALING"] = Tuple.Create("NC_Saldatura_Laterale", "Side Sealing", new[] { "side sealing", "sigillatura", "saldatura" }),
                ["SHAPE_TOP"] = Tuple.Create("NC_ShapeTop", "Shape Top", new[] { "shape top", "forma superiore" }),
                ["SHAPE_BOTTOM"] = Tuple.Create("NC_ShapeBottom", "Shape Bottom", new[] { "shape bottom", "forma inferiore" }),
                ["SURFACE_CHECK"] = Tuple.Create("NC_SurfaceCheck", "Surface Check", new[] { "surface", "sporco" }),
                ["THREED_PROFILE"] = Tuple.Create("NCThreeDProfile", "Top3D Profile", new[] { "top3d profile", "height profile", "profilo top3d" }),
                ["AI_CLASSIFICATION_TOP"] = Tuple.Create("NC_TopAiClassification", "AI Classification Top/Top3D", new[] { "ai classification failed [top" }),
                ["AI_CLASSIFICATION_SIDE"] = Tuple.Create("NC_SideAiClassification", "AI Classification Side/Left", new[] { "ai classification failed [side", "ai classification failed [left" }),
                ["AI_CLASSIFICATION_FRONT"] = Tuple.Create("NC_FrontAiClassification", "AI Classification Front", new[] { "ai classification failed [front" }),
                ["AI_CLASSIFICATION_REAR"] = Tuple.Create("NC_RearAiClassification", "AI Classification Rear", new[] { "ai classification failed [rear" }),
                ["AI_CLASSIFICATION_RIGHT"] = Tuple.Create("NC_RightAiClassification", "AI Classification Right", new[] { "ai classification failed [right" }),
                ["AI_CLASSIFICATION_BOTTOM"] = Tuple.Create("NC_BottomAiClassification", "AI Classification Bottom", new[] { "ai classification failed [bottom" })
            };

        private static readonly string[] OptionalTopThreeDProfileColumns =
        {
            "ThreeDHeightMedianValue",
            "ThreeDHeightHighTailValue",
            "ThreeDHeightMaximumValue",
            "ThreeDHeightBulgeValue",
            "ThreeDHeightValidPixelRatio",
            "NCThreeDProfile"
        };

        private static readonly string[] OptionalAiClassificationColumns =
        {
            "NC_TopAiClassification",
            "NC_SideAiClassification",
            "NC_FrontAiClassification",
            "NC_RearAiClassification",
            "NC_RightAiClassification",
            "NC_BottomAiClassification"
        };

        private readonly InspectorDataSourceProfile _profile;
        private readonly string _pieceTableName;
        private readonly string _productionTableName;

        public MySqlPieceHistoryRepository(InspectorDataSourceProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _pieceTableName = ValidateIdentifier(profile.PieceTableName, nameof(profile.PieceTableName));
            _productionTableName = ValidateIdentifier(profile.ProductionTableName, nameof(profile.ProductionTableName));
        }

        public Task<IReadOnlyList<PieceRecordSummary>> GetRecentRejectedPiecesAsync(int count, CancellationToken cancellationToken = default(CancellationToken))
        {
            var query = new PieceHistoryQuery
            {
                OnlyRejectedPieces = true,
                MaxResults = count
            };

            return QueryPiecesAsync(query, cancellationToken);
        }

        public async Task<IReadOnlyList<PieceRecordSummary>> QueryPiecesAsync(PieceHistoryQuery query, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            var pieces = new List<PieceRecordSummary>();

            using (var connection = new MySqlConnection(BuildConnectionString()))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                HashSet<string> availableColumns = await LoadPieceColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
                string sql = BuildQuerySql(query, availableColumns);

                using (var command = new MySqlCommand(sql, connection))
                {
                    FillParameters(command, query);

                    using (var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            pieces.Add(MapPiece(reader));
                    }
                }
            }

            return pieces;
        }

        public async Task<bool> CheckConnectionAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                using (var connection = new MySqlConnection(BuildConnectionString()))
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return connection.State == System.Data.ConnectionState.Open;
                }
            }
            catch
            {
                return false;
            }
        }

        private string BuildQuerySql(PieceHistoryQuery query, ISet<string> availableColumns)
        {
            var where = new List<string>();
            if (query.OnlyRejectedPieces)
            {
                if (string.Equals(_profile.RejectedPieceMode, "ClassificationRejected", StringComparison.OrdinalIgnoreCase))
                    where.Add("g.`Esito_Classificazione` IN (0, 3)");
                else
                    where.Add("g.`Espulsione_Comandata` = 1");
            }

            if (query.RequireSavedImages)
                where.Add("g.`PieceData` IS NOT NULL AND TRIM(g.`PieceData`) <> ''");

            if (!string.IsNullOrWhiteSpace(query.RecipeName))
                where.Add("g.`Ricetta` = @recipeName");

            if (!string.IsNullOrWhiteSpace(query.ProductName))
                where.Add("g.`Prodotto` = @productName");

            if (!string.IsNullOrWhiteSpace(query.OperatorName))
                where.Add("g.`Operatore` = @operatorName");

            if (query.FromUtc.HasValue)
                where.Add("g.`DataeOra` >= @fromUtc");

            if (query.ToUtc.HasValue)
                where.Add("g.`DataeOra` <= @toUtc");

            if (query.SelectedInspectionCodes.Count > 0)
            {
                List<string> selectedInspectionFilters = BuildInspectionFilters(
                    query.SelectedInspectionCodes,
                    availableColumns);
                if (selectedInspectionFilters.Count > 0)
                    where.Add("(" + string.Join(" OR ", selectedInspectionFilters) + ")");
            }

            string whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
            string optionalInspectionProjection = string.Join(
                "," + Environment.NewLine + "    ",
                OptionalTopThreeDProfileColumns
                    .Concat(OptionalAiClassificationColumns)
                    .Select(columnName => BuildOptionalColumnProjection(availableColumns, columnName)));

            return string.Format(CultureInfo.InvariantCulture, @"
SELECT
    g.`Id`,
    g.`IdProduzione`,
    g.`DataeOra`,
    g.`Operatore`,
    g.`Prodotto`,
    g.`Ricetta`,
    g.`Esito_Classificazione`,
    g.`Espulsione_Comandata`,
    g.`DataHostnames`,
    g.`PieceData`,
    g.`InspectionStatusDetails`,
    {3},
    g.`NC_Loghi_Immagini`,
    g.`NC_Centratura_Logo`,
    g.`NC_Alette_Aperte`,
    g.`NC_Heigth`,
    g.`NC_Saldatura_Laterale`,
    g.`NC_ShapeTop`,
    g.`NC_ShapeBottom`,
    g.`NC_SurfaceCheck`,
    p.`Impianto`,
    p.`Lotto`,
    p.`Turno`,
    CAST(p.`TimeCreationIdpro` AS CHAR(19)) AS `TimeCreationIdpro`,
    CAST(p.`TimeInizio` AS CHAR(19)) AS `TimeInizio`,
    CASE
        WHEN CAST(p.`TimeFine` AS CHAR(19)) = '0000-00-00 00:00:00' THEN NULL
        ELSE CAST(p.`TimeFine` AS CHAR(19))
    END AS `TimeFine`
FROM `{0}` g
LEFT JOIN `{1}` p
    ON p.`IdProduzione` = g.`IdProduzione`
    AND (p.`IsDeleted` = FALSE OR p.`IsDeleted` IS NULL)
{2}
ORDER BY g.`DataeOra` DESC
LIMIT @maxResults;", _pieceTableName, _productionTableName, whereClause, optionalInspectionProjection);
        }

        private async Task<HashSet<string>> LoadPieceColumnsAsync(
            MySqlConnection connection,
            CancellationToken cancellationToken)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const string sql = @"
SELECT `COLUMN_NAME`
FROM `INFORMATION_SCHEMA`.`COLUMNS`
WHERE `TABLE_SCHEMA` = @schemaName
  AND `TABLE_NAME` = @tableName;";

            using (var command = new MySqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@schemaName", _profile.DatabaseName);
                command.Parameters.AddWithValue("@tableName", _pieceTableName);
                using (var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (!reader.IsDBNull(0))
                        {
                            columns.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return columns;
        }

        private static string BuildOptionalColumnProjection(ISet<string> availableColumns, string columnName)
        {
            return availableColumns != null && availableColumns.Contains(columnName)
                ? "g.`" + columnName + "` AS `" + columnName + "`"
                : "NULL AS `" + columnName + "`";
        }

        private void FillParameters(MySqlCommand command, PieceHistoryQuery query)
        {
            command.Parameters.AddWithValue("@maxResults", Math.Max(1, query.MaxResults));

            if (!string.IsNullOrWhiteSpace(query.RecipeName))
                command.Parameters.AddWithValue("@recipeName", query.RecipeName);

            if (!string.IsNullOrWhiteSpace(query.ProductName))
                command.Parameters.AddWithValue("@productName", query.ProductName);

            if (!string.IsNullOrWhiteSpace(query.OperatorName))
                command.Parameters.AddWithValue("@operatorName", query.OperatorName);

            if (query.FromUtc.HasValue)
                command.Parameters.AddWithValue("@fromUtc", query.FromUtc.Value);

            if (query.ToUtc.HasValue)
                command.Parameters.AddWithValue("@toUtc", query.ToUtc.Value);

            int inspectionIndex = 0;
            foreach (string selectedCode in query.SelectedInspectionCodes)
            {
                Tuple<string, string, string[]> inspection;
                if (!InspectionMap.TryGetValue(selectedCode, out inspection))
                    continue;

                string parameterName = "@inspectionKeyword" + inspectionIndex.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(parameterName, "%" + inspection.Item3[0] + "%");
                inspectionIndex++;
            }
        }

        private PieceRecordSummary MapPiece(MySqlDataReader reader)
        {
            long pieceId = reader.GetInt64("Id");
            string rawPiecePath = GetNullableString(reader, "PieceData");
            string resolvedPiecePath = ResolvePiecePath(rawPiecePath);
            string dataHostnames = GetNullableString(reader, "DataHostnames");

            var piece = new PieceRecordSummary
            {
                PieceId = pieceId,
                ProductionId = reader.GetInt64("IdProduzione"),
                Timestamp = reader.GetDateTime("DataeOra"),
                OperatorName = GetNullableString(reader, "Operatore"),
                ProductName = GetNullableString(reader, "Prodotto"),
                RecipeName = GetNullableString(reader, "Ricetta"),
                ClassificationOutcome = GetNullableInt(reader, "Esito_Classificazione"),
                IsRejected = ComputeIsRejected(reader),
                PieceDataPath = resolvedPiecePath,
                DataHostnames = string.IsNullOrWhiteSpace(dataHostnames) ? _profile.DataHostnames : dataHostnames,
                InspectionStatusDetails = GetNullableString(reader, "InspectionStatusDetails"),
                SourceTable = _pieceTableName
            };

            piece.AdditionalFields["RawPieceData"] = rawPiecePath;
            piece.AdditionalFields["ResolvedPieceData"] = resolvedPiecePath;
            piece.AdditionalFields["DataHostnames"] = piece.DataHostnames;
            piece.AdditionalFields["PieceData"] = piece.PieceDataPath;
            piece.AdditionalFields["Impianto"] = GetNullableString(reader, "Impianto");
            piece.AdditionalFields["Lotto"] = GetNullableString(reader, "Lotto");
            piece.AdditionalFields["Turno"] = GetNullableString(reader, "Turno");
            piece.AdditionalFields["TimeCreationIdpro"] = GetNullableDateTimeString(reader, "TimeCreationIdpro");
            piece.AdditionalFields["TimeInizio"] = GetNullableDateTimeString(reader, "TimeInizio");
            piece.AdditionalFields["TimeFine"] = GetNullableDateTimeString(reader, "TimeFine");
            piece.AdditionalFields["Espulsione_Comandata"] = GetNullableInt(reader, "Espulsione_Comandata")?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            piece.AdditionalFields["Esito_Classificazione"] = piece.ClassificationOutcome?.ToString() ?? string.Empty;
            piece.AdditionalFields["ThreeDHeightMedianValue"] = FormatNullableDouble(GetNullableDouble(reader, "ThreeDHeightMedianValue"), "0.###");
            piece.AdditionalFields["ThreeDHeightHighTailValue"] = FormatNullableDouble(GetNullableDouble(reader, "ThreeDHeightHighTailValue"), "0.###");
            piece.AdditionalFields["ThreeDHeightMaximumValue"] = FormatNullableDouble(GetNullableDouble(reader, "ThreeDHeightMaximumValue"), "0.###");
            piece.AdditionalFields["ThreeDHeightBulgeValue"] = FormatNullableDouble(GetNullableDouble(reader, "ThreeDHeightBulgeValue"), "0.###");
            piece.AdditionalFields["ThreeDHeightValidPixelRatio"] = FormatNullableDouble(GetNullableDouble(reader, "ThreeDHeightValidPixelRatio"), "0.#####");
            piece.AdditionalFields["Top3DProfileSummary"] = ExtractTopThreeDProfileSummary(piece.InspectionStatusDetails);

            AddInspectionResultsFromFlags(piece, reader);
            AddInspectionResultsFromDetails(piece);

            if (piece.ClassificationOutcome == 3)
            {
                piece.InspectionResults.Insert(0, new InspectionResultDescriptor
                {
                    Code = "UNCLASSIFIED",
                    DisplayName = "Non classificato",
                    Status = "UNCLASSIFIED",
                    Value = "3",
                    Details = piece.InspectionStatusDetails
                });
            }

            if (piece.InspectionResults.Count == 0)
            {
                piece.InspectionResults.Add(new InspectionResultDescriptor
                {
                    Code = piece.IsRejected ? "REJECTED_PIECE" : "PIECE",
                    DisplayName = piece.IsRejected ? "Rejected Piece" : "Piece",
                    Status = piece.IsRejected ? "Rejected" : "Accepted",
                    Details = piece.InspectionStatusDetails
                });
            }

            return piece;
        }

        private bool ComputeIsRejected(MySqlDataReader reader)
        {
            int? expulsionCommanded = GetNullableInt(reader, "Espulsione_Comandata");
            if (expulsionCommanded.HasValue)
                return expulsionCommanded.Value == 1;

            int? classification = GetNullableInt(reader, "Esito_Classificazione");
            return classification.HasValue && classification.Value == 0;
        }

        private List<string> BuildInspectionFilters(
            IReadOnlyList<string> selectedInspectionCodes,
            ISet<string> availableColumns)
        {
            var filters = new List<string>();
            int inspectionIndex = 0;

            foreach (string selectedCode in selectedInspectionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Tuple<string, string, string[]> inspection;
                if (!InspectionMap.TryGetValue(selectedCode, out inspection))
                    continue;

                string keywordParameterName = "@inspectionKeyword" + inspectionIndex.ToString(CultureInfo.InvariantCulture);
                if (availableColumns != null && availableColumns.Contains(inspection.Item1))
                {
                    filters.Add("g.`" + inspection.Item1 + "` = 0");
                }
                filters.Add("LOWER(COALESCE(g.`InspectionStatusDetails`, '')) LIKE LOWER(" + keywordParameterName + ")");
                inspectionIndex++;
            }

            return filters;
        }

        private void AddInspectionResultsFromFlags(PieceRecordSummary piece, MySqlDataReader reader)
        {
            foreach (var entry in InspectionMap)
            {
                int? value = GetNullableInt(reader, entry.Value.Item1);
                if (!value.HasValue)
                    continue;

                piece.AdditionalFields[entry.Value.Item1] = value.Value.ToString(CultureInfo.InvariantCulture);

                if (string.Equals(entry.Key, "THREED_PROFILE", StringComparison.OrdinalIgnoreCase))
                {
                    piece.InspectionResults.Add(new InspectionResultDescriptor
                    {
                        Code = entry.Key,
                        DisplayName = entry.Value.Item2,
                        Status = ResolveTopThreeDProfileStatus(
                            value.Value,
                            piece.AdditionalFields["Top3DProfileSummary"]),
                        Value = BuildTopThreeDProfileValue(piece),
                        Details = piece.AdditionalFields["Top3DProfileSummary"]
                    });
                    continue;
                }

                if (value.Value == 1)
                    continue;

                piece.InspectionResults.Add(new InspectionResultDescriptor
                {
                    Code = entry.Key,
                    DisplayName = entry.Value.Item2,
                    Status = TranslateFlagStatus(value.Value),
                    Value = value.Value.ToString(CultureInfo.InvariantCulture),
                    Details = piece.InspectionStatusDetails
                });
            }
        }

        private static string ResolveTopThreeDProfileStatus(int value, string profileSummary)
        {
            if (!string.IsNullOrWhiteSpace(profileSummary))
            {
                Match stateMatch = Regex.Match(
                    profileSummary,
                    @"\bstate=(?<state>GOOD|NO\s*GOOD|NOGOOD|INVALID|MONITOR|WAIT)\b",
                    RegexOptions.IgnoreCase);
                if (stateMatch.Success)
                {
                    string state = stateMatch.Groups["state"].Value
                        .Replace(" ", string.Empty)
                        .ToUpperInvariant();
                    return state == "NOGOOD" ? "NO GOOD" : state;
                }
            }

            switch (value)
            {
                case 0: return "NO GOOD";
                case 1: return "GOOD";
                case 3: return "UNCLASSIFIED";
                case 4: return "MONITOR";
                default: return "STATE " + value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string BuildTopThreeDProfileValue(PieceRecordSummary piece)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "median={0} mm; highTail={1} mm; maximum={2} mm; bulge={3} mm; validPixels={4}",
                ReadAdditionalField(piece, "ThreeDHeightMedianValue"),
                ReadAdditionalField(piece, "ThreeDHeightHighTailValue"),
                ReadAdditionalField(piece, "ThreeDHeightMaximumValue"),
                ReadAdditionalField(piece, "ThreeDHeightBulgeValue"),
                FormatRatioAdditionalField(piece, "ThreeDHeightValidPixelRatio"));
        }

        private static string ReadAdditionalField(PieceRecordSummary piece, string key)
        {
            if (piece?.AdditionalFields != null &&
                piece.AdditionalFields.TryGetValue(key, out string value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return "--";
        }

        private static string FormatRatioAdditionalField(PieceRecordSummary piece, string key)
        {
            string raw = ReadAdditionalField(piece, key);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio))
            {
                return ratio.ToString("P1", CultureInfo.InvariantCulture);
            }

            return "--";
        }

        private static string ExtractTopThreeDProfileSummary(string details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return string.Empty;
            }

            return details
                .Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(fragment => fragment.Trim())
                .FirstOrDefault(fragment =>
                    fragment.StartsWith("Top3D profile:", StringComparison.OrdinalIgnoreCase))
                ?? string.Empty;
        }

        private void AddInspectionResultsFromDetails(PieceRecordSummary piece)
        {
            if (string.IsNullOrWhiteSpace(piece.InspectionStatusDetails) ||
                string.Equals(piece.InspectionStatusDetails, "OK", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string[] fragments = piece.InspectionStatusDetails.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string fragmentValue in fragments)
            {
                string fragment = fragmentValue.Trim();
                if (string.IsNullOrWhiteSpace(fragment))
                    continue;

                string normalized = fragment.ToLowerInvariant();
                var matchedEntry = InspectionMap.FirstOrDefault(m => m.Value.Item3.Any(keyword => normalized.Contains(keyword)));
                string code = string.IsNullOrWhiteSpace(matchedEntry.Key) ? "DETAIL" : matchedEntry.Key;
                string displayName = string.IsNullOrWhiteSpace(matchedEntry.Key) ? "Inspection Detail" : matchedEntry.Value.Item2;

                bool alreadyPresent = piece.InspectionResults.Any(existing =>
                    string.Equals(existing.Code, code, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Details, fragment, StringComparison.OrdinalIgnoreCase));

                if (alreadyPresent)
                    continue;

                piece.InspectionResults.Add(new InspectionResultDescriptor
                {
                    Code = code,
                    DisplayName = displayName,
                    Status = piece.IsRejected ? "Rejected" : "Reported",
                    Details = fragment
                });
            }
        }

        private string ResolvePiecePath(string rawPiecePath)
        {
            if (string.IsNullOrWhiteSpace(rawPiecePath))
                return string.Empty;

            string normalizedPath = rawPiecePath.Trim().Trim('"');

            string candidate = normalizedPath;
            if (!Path.IsPathRooted(candidate))
            {
                if (!_profile.ResolveRelativePiecePathsFromImageRoot || string.IsNullOrWhiteSpace(_profile.ImageRootPath))
                    return candidate;

                candidate = Path.GetFullPath(Path.Combine(_profile.ImageRootPath, candidate));
            }

            if (Directory.Exists(candidate))
                return candidate;

            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate) ?? candidate;

            string remapped = TryRemapToCurrentImageRoot(normalizedPath);
            if (!string.IsNullOrWhiteSpace(remapped))
                return remapped;

            return candidate;
        }

        private string TryRemapToCurrentImageRoot(string rawPiecePath)
        {
            if (string.IsNullOrWhiteSpace(rawPiecePath) || string.IsNullOrWhiteSpace(_profile.ImageRootPath))
                return string.Empty;

            string normalizedRoot = Path.GetFullPath(_profile.ImageRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] anchors = { "\\Pieces\\", "/Pieces/" };

            foreach (string anchor in anchors)
            {
                int index = rawPiecePath.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    continue;

                string relativeSuffix = rawPiecePath.Substring(index + anchor.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.IsNullOrWhiteSpace(relativeSuffix))
                    continue;

                string remapped = Path.Combine(normalizedRoot, relativeSuffix);
                if (Directory.Exists(remapped))
                    return remapped;

                if (File.Exists(remapped))
                    return Path.GetDirectoryName(remapped) ?? remapped;
            }

            return string.Empty;
        }

        private string BuildConnectionString()
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = _profile.DatabaseHost,
                Port = (uint)Math.Max(1, _profile.DatabasePort),
                Database = _profile.DatabaseName,
                UserID = _profile.DatabaseUser,
                Password = _profile.DatabasePassword,
                SslMode = _profile.DisableSsl ? MySqlSslMode.Disabled : MySqlSslMode.Preferred,
                AllowUserVariables = true,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true,
                PersistSecurityInfo = false
            };

            return builder.ConnectionString;
        }

        private static string ValidateIdentifier(string identifier, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(identifier) || !IdentifierRegex.IsMatch(identifier))
                throw new InvalidOperationException("Invalid SQL identifier in profile property '" + propertyName + "'.");

            return identifier;
        }

        private static string GetNullableString(MySqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static int? GetNullableInt(MySqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? (int?)null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static double? GetNullableDouble(MySqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? (double?)null
                : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static string FormatNullableDouble(double? value, string format)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? value.Value.ToString(format, CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string GetNullableDateTimeString(MySqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return string.Empty;

            object value = reader.GetValue(ordinal);
            if (value is DateTime)
                return ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            return value != null ? value.ToString() : string.Empty;
        }

        private static string TranslateFlagStatus(int value)
        {
            switch (value)
            {
                case 0: return "Rejected";
                case 1: return "Passed";
                case 3: return "Unclassified";
                case 4: return "Not checked";
                default: return "State " + value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
