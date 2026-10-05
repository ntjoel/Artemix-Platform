using MySql.Data.MySqlClient;
using Mysqlx.Crud;
using NLog;
using Org.BouncyCastle.Utilities.Collections;
using QtisVisionPanel.Database.ProductionRecord;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static Google.Protobuf.Compiler.CodeGeneratorResponse.Types;
using static QtisVisionPanel.Cls_Config.Calss_structure.RecipeParameters;

namespace QtisVisionPanel.Database
{
    /// <summary>
    /// Applies incremental schema migrations to the MySQL database at startup.
    ///
    /// Each public method checks whether the target columns or indexes already exist
    /// before issuing any ALTER TABLE, so the same method is safe to call on every
    /// HMI start regardless of whether the schema is already up to date.
    /// Missing columns are added as nullable to preserve compatibility with
    /// production databases that have not yet been upgraded.
    /// </summary>
    public class Cls_InitializzeDb
    {
       // private readonly MainWindow.ConfigManager configManager;
        // Lazy access: safe even if Cls_InitializzeDb is instantiated before config is loaded.
        private ILogger logger => MainWindow.logger;
        private string dbName => MainWindow.configManager?.Config?.MySqlConnection?.Db;
        //public Cls_InitializzeDb( ILogger logger)
        //{
           
        //    this.logger = logger;
        //    this.dbName = MainWindow.configManager.Config.MySqlConnection.Db;
        //}
        public async Task AddNewColumnsToTblGenerale()
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port};Connection Timeout=5;";

            try
            {
                using (var conn = new MySqlConnection(connectionString))
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6)))
                {
                    await conn.OpenAsync(cts.Token);

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_SurfaceCheck",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_SurfaceCheck INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_BottomSealing",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_BottomSealing INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_TrappedPaper",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_TrappedPaper INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "InspectionStatusDetails",
                        "ALTER TABLE tblgenerale ADD COLUMN InspectionStatusDetails TEXT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "TopClassificationLabel",
                        "ALTER TABLE tblgenerale ADD COLUMN TopClassificationLabel VARCHAR(255) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "TopClassificationScore",
                        "ALTER TABLE tblgenerale ADD COLUMN TopClassificationScore DOUBLE NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "SideClassificationLabel",
                        "ALTER TABLE tblgenerale ADD COLUMN SideClassificationLabel VARCHAR(255) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "SideClassificationScore",
                        "ALTER TABLE tblgenerale ADD COLUMN SideClassificationScore DOUBLE NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "FrontClassificationLabel",
                        "ALTER TABLE tblgenerale ADD COLUMN FrontClassificationLabel VARCHAR(255) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "FrontClassificationScore",
                        "ALTER TABLE tblgenerale ADD COLUMN FrontClassificationScore DOUBLE NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "RearClassificationLabel",
                        "ALTER TABLE tblgenerale ADD COLUMN RearClassificationLabel VARCHAR(255) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "RearClassificationScore",
                        "ALTER TABLE tblgenerale ADD COLUMN RearClassificationScore DOUBLE NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "RightClassificationLabel",
                        "ALTER TABLE tblgenerale ADD COLUMN RightClassificationLabel VARCHAR(255) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "RightClassificationScore",
                        "ALTER TABLE tblgenerale ADD COLUMN RightClassificationScore DOUBLE NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "BottomClassificationLabel",
                        "ALTER TABLE tblgenerale ADD COLUMN BottomClassificationLabel VARCHAR(255) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "BottomClassificationScore",
                        "ALTER TABLE tblgenerale ADD COLUMN BottomClassificationScore DOUBLE NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_TopAiClassification",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_TopAiClassification INT(1) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_SideAiClassification",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_SideAiClassification INT(1) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_FrontAiClassification",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_FrontAiClassification INT(1) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_RearAiClassification",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_RearAiClassification INT(1) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_RightAiClassification",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_RightAiClassification INT(1) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_BottomAiClassification",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_BottomAiClassification INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "TraceabilityDetected",
                        "ALTER TABLE tblgenerale ADD COLUMN TraceabilityDetected INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "TraceabilityCode",
                        "ALTER TABLE tblgenerale ADD COLUMN TraceabilityCode VARCHAR(255) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "TraceabilityExpectedPrefix",
                        "ALTER TABLE tblgenerale ADD COLUMN TraceabilityExpectedPrefix VARCHAR(255) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_Traceability",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_Traceability INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeightNominalValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeightNominalValue FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeightMeasureValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeightMeasureValue FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeight_Minimum",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeight_Minimum FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeight_Maximum",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeight_Maximum FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_ThreeDHeight",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_ThreeDHeight INT(1) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeightMedianValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeightMedianValue FLOAT(10,3) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeightHighTailValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeightHighTailValue FLOAT(10,3) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeightMaximumValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeightMaximumValue FLOAT(10,3) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeightBulgeValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeightBulgeValue FLOAT(10,3) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDHeightValidPixelRatio",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDHeightValidPixelRatio FLOAT(10,5) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NCThreeDProfile",
                        "ALTER TABLE tblgenerale ADD COLUMN NCThreeDProfile INT NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDWidthNominalValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDWidthNominalValue FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDWidthMeasureValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDWidthMeasureValue FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDWidth_Minimum",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDWidth_Minimum FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDWidth_Maximum",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDWidth_Maximum FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_ThreeDWidth",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_ThreeDWidth INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDLengthNominalValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDLengthNominalValue FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDLengthMeasureValue",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDLengthMeasureValue FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDLength_Minimum",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDLength_Minimum FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "ThreeDLength_Maximum",
                        "ALTER TABLE tblgenerale ADD COLUMN ThreeDLength_Maximum FLOAT(10,2) NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblgenerale", "NC_ThreeDLength",
                        "ALTER TABLE tblgenerale ADD COLUMN NC_ThreeDLength INT(1) NULL DEFAULT NULL");

                    await EnsureColumnAsync(conn, config.Db, "tblproduzione", "IsDeleted",
                        "ALTER TABLE tblproduzione ADD COLUMN IsDeleted BOOLEAN DEFAULT FALSE");

                    await EnsureColumnAsync(conn, config.Db, "tblproduzione", "UpdatedAt",
                        "ALTER TABLE tblproduzione ADD COLUMN UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

                    await EnsureColumnAsync(conn, config.Db, "tblproduzione", "CreatedAt",
                        "ALTER TABLE tblproduzione ADD COLUMN CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP");

                    await EnsureColumnAsync(conn, config.Db, "tblproduzione", "RecipeParamerterFront",
                        "ALTER TABLE tblproduzione ADD COLUMN RecipeParamerterFront JSON NULL DEFAULT NULL");
                    await EnsureColumnAsync(conn, config.Db, "tblproduzione", "RecipeParamerterTop3D",
                        "ALTER TABLE tblproduzione ADD COLUMN RecipeParamerterTop3D JSON NULL DEFAULT NULL");

                    await EnsureGlobalCounterAsync(conn, "THREED_HEIGHT", "Difetti altezza profilometro 3D");
                    await EnsureGlobalCounterAsync(conn, "THREED_WIDTH", "Difetti larghezza profilometro 3D");
                    await EnsureGlobalCounterAsync(conn, "THREED_LENGTH", "Difetti lunghezza profilometro 3D");
                    await EnsureGlobalCounterAsync(conn, "BOTTOM_SEALING", "Difetti saldatura inferiore");
                    await EnsureGlobalCounterAsync(conn, "TRAPPED_PAPER", "Difetti carta intrappolata nella saldatura");
                    await EnsureGlobalCounterAsync(conn, "UNCLASSIFIED", "Pezzi non classificati per errore elaborazione visione");

                    await EnsureInspectionFeatureAsync(conn, "ThreeDHeight", "Altezza 3D", true, "3DCheck");
                    await EnsureInspectionFeatureAsync(conn, "ThreeDWidth", "Larghezza 3D", true, "3DCheck");
                    await EnsureInspectionFeatureAsync(conn, "ThreeDLength", "Lunghezza 3D", true, "3DCheck");
                    await EnsureInspectionFeatureAsync(conn, "FrontTraceability", "Tracciabilita front", false, "3DCheck");
                    await EnsureInspectionFeatureAsync(conn, "BottomSealing", "Saldatura inferiore", true, "Packs");
                    await EnsureInspectionFeatureAsync(conn, "TrappedPaper", "Carta intrappolata", true, "Packs");
                    await EnsurePowerFlex525HistoryTableAsync(conn);
                    await OpcUaConfigurationRepository.CreateTableIfNotExistsAsync(conn);
                    await MultiShotParamHistoryRepository.CreateTableIfNotExistsAsync(conn);

                    MainWindow.logger?.Info("Verifica colonne database completata con successo");
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Errore nell'aggiungere nuove colonne a tblgenerale: " + ex.Message);
            }
        }

        private static async Task EnsureColumnAsync(MySqlConnection conn, string databaseName, string tableName, string columnName, string alterSql)
        {
            if (await ColumnExistsAsync(conn, databaseName, tableName, columnName))
            {
                return;
            }

            using (var alterCmd = new MySqlCommand(alterSql, conn))
            {
                // Fase 0 stabilita': ALTER TABLE su tabelle grandi puo' essere lento ma non
                // deve bloccare l'avvio a tempo indefinito.
                alterCmd.CommandTimeout = 120;
                await alterCmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task<bool> ColumnExistsAsync(MySqlConnection conn, string databaseName, string tableName, string columnName)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @schema
                  AND TABLE_NAME = @table
                  AND COLUMN_NAME = @column;";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@schema", databaseName);
                cmd.Parameters.AddWithValue("@table", tableName);
                cmd.Parameters.AddWithValue("@column", columnName);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
        }

        private static async Task EnsureGlobalCounterAsync(MySqlConnection conn, string feature, string description)
        {
            const string sql = @"
                INSERT IGNORE INTO tblglobalcounters (Feature, Description, Counter)
                VALUES (@feature, @description, 0);";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@feature", feature);
                cmd.Parameters.AddWithValue("@description", description ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task EnsureInspectionFeatureAsync(MySqlConnection conn, string feature, string description, bool enabled, string machineType)
        {
            const string sql = @"
                INSERT IGNORE INTO cfg_inspection (Feature, Description, IsEnabled, MachineType)
                VALUES (@feature, @description, @enabled, @machineType);";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@feature", feature);
                cmd.Parameters.AddWithValue("@description", description ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@enabled", enabled);
                cmd.Parameters.AddWithValue("@machineType", machineType);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task EnsurePowerFlex525HistoryTableAsync(MySqlConnection conn)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS `tblpowerflex525_history` (
                    `Id` BIGINT NOT NULL AUTO_INCREMENT,
                    `Timestamp` DATETIME NOT NULL,
                    `User` VARCHAR(100) NULL,
                    `Role` VARCHAR(100) NULL,
                    `DriveIp` VARCHAR(64) NULL,
                    `ParameterNumber` INT NOT NULL,
                    `ParameterCode` VARCHAR(16) NULL,
                    `ParameterName` VARCHAR(255) NULL,
                    `OldValue` DOUBLE NULL,
                    `NewValue` DOUBLE NULL,
                    `Unit` VARCHAR(32) NULL,
                    `Source` VARCHAR(100) NULL,
                    `Result` VARCHAR(50) NULL,
                    PRIMARY KEY (`Id`),
                    KEY `idx_powerflex525_timestamp` (`Timestamp`),
                    KEY `idx_powerflex525_parameter` (`ParameterNumber`)
                ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static bool ColumnExists(MySqlConnection conn, string databaseName, string tableName, string columnName)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @schema
                  AND TABLE_NAME = @table
                  AND COLUMN_NAME = @column;";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@schema", databaseName);
                cmd.Parameters.AddWithValue("@table", tableName);
                cmd.Parameters.AddWithValue("@column", columnName);

                var result = cmd.ExecuteScalar();
                return Convert.ToInt32(result) > 0;
            }
        }
        public async Task Initialize()
        {
            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                if (!int.TryParse(config.port, out int port))
                {
                    logger?.Error("Porta MySQL non valida: " + config.port);
                    return;
                }

                using (var dbManager = new DbConnectionManager(
                    config.Host,
                    config.User,
                    config.Password,
                    port))
                {
                    if (await dbManager.OpenConnectionAsync())
                    {
                        await CreateDatabase(dbManager);
                        await CreateTblGlobalCounters(dbManager);
                        await CreateTblGenerale(dbManager);
                        await CreateTblProduzione(dbManager);
                        await CreateTblLogEvent(dbManager);
                        await CreateCfgInspectionTable(dbManager);
                        await CreateAlarmEventsTableAsync(dbManager);
                        await CreateAuditLogTableAsync(dbManager);
                        await CreateInspectionMeasurementsTable(dbManager);
                        await CreateHealthSnapshotsTable(dbManager);
                        await CreateTrainingSamplesTable(dbManager);
                        await CreateRetentionEventAsync(dbManager);
                        await CheckTableSizeAsync(dbManager);
                        dbManager.CloseConnection();
                    }
                    else
                    {
                        logger?.Error("Impossibile aprire la connessione al database.");
                    }
                }
            }
            catch (MySqlException ex)
            {
                logger?.Error($"Failed to initialize database: {ex.Message}\n{ex.StackTrace}");
            }
        }
        private async Task CreateTblGlobalCounters(DbConnectionManager dbManager)
        {
            string query = $@"
            CREATE TABLE IF NOT EXISTS `tblglobalcounters` (
                `Id` bigint(10) NOT NULL AUTO_INCREMENT,
                `Feature` varchar(100) NOT NULL,
                `Counter` int(11) NOT NULL DEFAULT 0,
                `Description` varchar(255) DEFAULT NULL,
                `LastUpdated` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (`Id`),
                UNIQUE KEY `uk_Feature` (`Feature`),
                KEY `idx_LastUpdated` (`LastUpdated`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;

            INSERT IGNORE INTO `tblglobalcounters` (Feature, Description, Counter) VALUES
                ('TOTAL', 'Totale ispezioni', 0),
                ('GOOD', 'Prodotti conformi', 0),
                ('NOGOOD', 'Prodotti non conformi', 0),
                ('UNCLASSIFIED', 'Pezzi non classificati per errore elaborazione visione', 0),
                ('LOGO', 'Difetti logo/stampa', 0),
                ('SHAPE_TOP', 'Difetti forma superiore', 0),
                ('OPEN_FLAPS', 'Difetti alette aperte', 0),
                ('SURFACE_CHECK', 'Difetti controllo superficie', 0),
                ('PRINT_CENTERING', 'Difetti centratura stampa', 0),
                ('SIDE_SEALING', 'Difetti sigillatura laterale', 0),
                ('SHAPE_SIDE', 'Difetti forma laterale', 0),
                ('HEIGHT', 'Difetti altezza', 0),
                ('TRACEABILITY', 'Difetti tracciabilita front', 0),
                ('BOTTOM_SEALING', 'Difetti saldatura inferiore', 0),
                ('TRAPPED_PAPER', 'Difetti carta intrappolata nella saldatura', 0);";

            await ExecuteQueryAsync(dbManager, query);
        }
        private async Task CreateTblGenerale(DbConnectionManager dbManager)
        {
            string query = @"
            CREATE TABLE IF NOT EXISTS `tblgenerale` (
                `Id` bigint(10) NOT NULL AUTO_INCREMENT,
                `IdProduzione` bigint(10) NOT NULL,
                `DataeOra` DATETIME NOT NULL,
                `Operatore` varchar(50) DEFAULT NULL,
                `Prodotto` varchar(100) DEFAULT NULL,
                `Ricetta` varchar(100) DEFAULT NULL,
                `Esito_Classificazione` INT(1) NULL DEFAULT NULL,
                `MinValueLogoToll` FLOAT(10,2) NULL DEFAULT NULL,
                `LogoMatchPerc` FLOAT(10,2) NULL DEFAULT NULL,
                `NC_Loghi_Immagini` INT(1) NULL DEFAULT NULL,
                `TopValueToMarker` FLOAT(10,2) NULL DEFAULT NULL,
                `LeftValueToMarker` FLOAT(10,2) NULL DEFAULT NULL,
                `RigthValueToMarker` FLOAT(10,2) NULL DEFAULT NULL,
                `BottomValueToMarker` FLOAT(10,2) NULL DEFAULT NULL,
                `NC_Centratura_Logo` INT(1) NULL DEFAULT NULL,
                `Area1FlapsDetected` FLOAT(10,2) NULL DEFAULT NULL,
                `Area2FlapsDetected` FLOAT(10,2) NULL DEFAULT NULL,
                `NC_Alette_Aperte` INT(1) NULL DEFAULT NULL,
                `HeigthNominalValue` FLOAT(10,2) NULL DEFAULT NULL,
                `HeigthMeasureValue` FLOAT(10,2) NULL DEFAULT NULL,
                `Heigth_Minimum` FLOAT(10,2) NULL DEFAULT NULL,
                `Heigth_Maximum` FLOAT(10,2) NULL DEFAULT NULL,
                `NC_Heigth` INT(1) NULL DEFAULT NULL,
                `MinAreaSealing` FLOAT(10,2) NULL DEFAULT NULL,
                `AreaSealing` FLOAT(10,2) NULL DEFAULT NULL,
                `NC_Saldatura_Laterale` INT(1) NULL DEFAULT NULL,
                `A_T_Value` FLOAT(10,2) NULL DEFAULT NULL,
                `A_T_Toll` FLOAT(10,2) NULL DEFAULT NULL,
                `A_T_topLeft` FLOAT(10,2) NULL DEFAULT NULL,
                `A_T_TopRigth` FLOAT(10,2) NULL DEFAULT NULL,
                `A_T_bottomLeft` FLOAT(10,2) NULL DEFAULT NULL,
                `A_T_BottomRigth` FLOAT(10,2) NULL DEFAULT NULL,
                `NC_ShapeTop` INT(1) NULL DEFAULT NULL,
                `A_B_Value` FLOAT(10,2) NULL DEFAULT NULL,
                `A_B_Toll` FLOAT(10,2) NULL DEFAULT NULL,
                `A_B_topLeft` FLOAT(10,2) NULL DEFAULT NULL,
                `A_B_TopRigth` FLOAT(10,2) NULL DEFAULT NULL,
                `A_B_bottomLeft` FLOAT(10,2) NULL DEFAULT NULL,
                `A_B_BottomRigth` FLOAT(10,2) NULL DEFAULT NULL,
                `NC_ShapeBottom` INT(1) NULL DEFAULT NULL,
                `Espulsione_Comandata` INT(1) NULL DEFAULT NULL,
                `DataHostnames` varchar(250) DEFAULT NULL,
                `PieceData` varchar(250) DEFAULT NULL,
                `InspectionStatusDetails` TEXT DEFAULT NULL,
                `TopClassificationLabel` varchar(255) DEFAULT NULL,
                `TopClassificationScore` DOUBLE DEFAULT NULL,
                `SideClassificationLabel` varchar(255) DEFAULT NULL,
                `SideClassificationScore` DOUBLE DEFAULT NULL,
                `FrontClassificationLabel` varchar(255) DEFAULT NULL,
                `FrontClassificationScore` DOUBLE DEFAULT NULL,
                `RearClassificationLabel` varchar(255) DEFAULT NULL,
                `RearClassificationScore` DOUBLE DEFAULT NULL,
                `RightClassificationLabel` varchar(255) DEFAULT NULL,
                `RightClassificationScore` DOUBLE DEFAULT NULL,
                `BottomClassificationLabel` varchar(255) DEFAULT NULL,
                `BottomClassificationScore` DOUBLE DEFAULT NULL,
                `NC_TopAiClassification` INT(1) NULL DEFAULT NULL,
                `NC_SideAiClassification` INT(1) NULL DEFAULT NULL,
                `NC_FrontAiClassification` INT(1) NULL DEFAULT NULL,
                `NC_RearAiClassification` INT(1) NULL DEFAULT NULL,
                `NC_RightAiClassification` INT(1) NULL DEFAULT NULL,
                `NC_BottomAiClassification` INT(1) NULL DEFAULT NULL,
                `NC_SurfaceCheck` INT(1) NULL DEFAULT NULL,
                `NC_BottomSealing` INT(1) NULL DEFAULT NULL,
                `NC_TrappedPaper` INT(1) NULL DEFAULT NULL,
                `TraceabilityDetected` INT(1) NULL DEFAULT NULL,
                `TraceabilityCode` varchar(255) DEFAULT NULL,
                `TraceabilityExpectedPrefix` varchar(255) DEFAULT NULL,
                `NC_Traceability` INT(1) NULL DEFAULT NULL,
                PRIMARY KEY (`Id`),
                KEY `idx_DataeOra` (`DataeOra`),
                KEY `idx_EsitoClass` (`Esito_Classificazione`),
                KEY `idx_IdProduzione` (`IdProduzione`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            await ExecuteQueryAsync(dbManager, query);

            // Aggiunge indici sulle istanze esistenti (idempotente)
            await EnsureIndexAsync(dbManager, "tblgenerale", "idx_EsitoClass",
                "ALTER TABLE tblgenerale ADD INDEX idx_EsitoClass (Esito_Classificazione)",
                "Esito_Classificazione");
            await EnsureIndexAsync(dbManager, "tblgenerale", "idx_IdProduzione",
                "ALTER TABLE tblgenerale ADD INDEX idx_IdProduzione (IdProduzione)",
                "IdProduzione");
            await EnsureIndexAsync(dbManager, "tblgenerale", "idx_DataeOra",
                "ALTER TABLE tblgenerale ADD INDEX idx_DataeOra (DataeOra)",
                "DataeOra");
        }

        private async Task EnsureIndexAsync(DbConnectionManager dbManager, string tableName, string indexName, string alterSql, params string[] requiredColumns)
        {
            try
            {
                var conn = dbManager.GetConnection(); // do NOT dispose — owned by dbManager

                foreach (var columnName in requiredColumns ?? Array.Empty<string>())
                {
                    if (!await ColumnExistsAsync(conn, tableName, columnName).ConfigureAwait(false))
                    {
                        logger?.Warn("INDEX_SKIPPED_MISSING_COLUMN|table={0}|index={1}|column={2}", tableName, indexName, columnName);
                        return;
                    }
                }

                string checkSql = $@"SELECT COUNT(*) FROM information_schema.STATISTICS
                    WHERE table_schema = DATABASE() AND table_name = @tbl AND index_name = @idx";
                using (var cmd = new MySqlCommand(checkSql, conn))
                {
                    cmd.Parameters.AddWithValue("@tbl", tableName);
                    cmd.Parameters.AddWithValue("@idx", indexName);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                    if (count == 0)
                    {
                        using (var alter = new MySqlCommand(alterSql, conn))
                        {
                            await alter.ExecuteNonQueryAsync();
                        }

                        logger?.Info("INDEX_CREATED|table={0}|index={1}", tableName, indexName);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "ENSURE_INDEX_FAILED|table={0}|index={1}", tableName, indexName);
            }
        }

        private static async Task<bool> ColumnExistsAsync(MySqlConnection conn, string tableName, string columnName)
        {
            const string sql = @"SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE table_schema = DATABASE() AND table_name = @tbl AND column_name = @col";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@tbl", tableName);
                cmd.Parameters.AddWithValue("@col", columnName);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false)) > 0;
            }
        }

        private static bool IsValidIdentifier(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   name.Length <= 64 &&
                   Regex.IsMatch(name, @"^[a-zA-Z0-9_]+$");
        }

        private async Task CreateDatabase(DbConnectionManager dbManager)
        {
            if (!IsValidIdentifier(dbName))
            {
                logger?.Error("DB_INVALID_NAME|name={0} — startup aborted", dbName);
                throw new InvalidOperationException($"Nome database non valido (solo lettere, cifre, underscore): '{dbName}'");
            }

            string query = $@"CREATE DATABASE IF NOT EXISTS `{dbName}`;
                         USE `{dbName}`;";

            await ExecuteQueryAsync(dbManager, query);
        }
        private async Task CreateTblLogEvent(DbConnectionManager dbManager)
        {
            string query = @"
            CREATE TABLE IF NOT EXISTS `tbllogevent` (
                `Id` bigint(10) NOT NULL AUTO_INCREMENT,
                `application` varchar(255) DEFAULT NULL,
                `timestamp` datetime(3) NOT NULL,
                `level` varchar(1) DEFAULT NULL,
                `log_id` varchar(255) NOT NULL,
                `info` json DEFAULT NULL,
                `export_type` int(1) DEFAULT NULL,
                PRIMARY KEY (`Id`),
                KEY `idx_DataeOra` (`timestamp`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            await ExecuteQueryAsync(dbManager, query);
        }
        private async Task CreateTblProduzione(DbConnectionManager dbManager)
        {
            string createTableQuery = $@"
            CREATE TABLE IF NOT EXISTS `tblproduzione` (
                `IdProduzione` bigint(10) NOT NULL AUTO_INCREMENT,
                `Impianto` varchar(50) DEFAULT NULL,
                `Lotto` varchar(15) DEFAULT NULL,
                `Turno` varchar(5) DEFAULT NULL,
                `Operatore` varchar(50) DEFAULT NULL,
                `Prodotto` varchar(100) DEFAULT NULL,
                `Ricetta` varchar(100) DEFAULT NULL,
                `RecipeParamerterTop` json DEFAULT NULL,
                `RecipeParamerterSide` json DEFAULT NULL,
                `RecipeParamerterFront` json DEFAULT NULL,
                `RecipeParamerterTop3D` json DEFAULT NULL,
                `LastParameter` json DEFAULT NULL,
                `ConfigParameter` json DEFAULT NULL,
                `TimeCreationIdpro` datetime NOT NULL,
                `TimeInizio` datetime NOT NULL,
                `TimeFine` datetime NULL DEFAULT NULL,
                `type` INT(1) NULL DEFAULT NULL,
                `IsDeleted` BOOLEAN DEFAULT FALSE,
                `UpdatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (`IdProduzione`)
            ) ENGINE=InnoDB AUTO_INCREMENT=10;";

            await ExecuteQueryAsync(dbManager, createTableQuery);
            await SanitizeTblProduzioneTimeFineAsync(dbManager);
        }

        private async Task SanitizeTblProduzioneTimeFineAsync(DbConnectionManager dbManager)
        {
            // Legacy rows may contain 0000-00-00 zero-date sentinels. We relax the
            // session sql_mode temporarily to normalize them to NULL.
            // NOTE: @variable syntax is NOT used in query strings because MySql.Data
            // treats @name as a parameter placeholder and throws when no value is bound.
            // sql_mode is read and restored entirely through C# string interpolation.
            string originalSqlMode = null;
            try
            {
                using (var cmd = new MySqlCommand("SELECT @@SESSION.sql_mode;", dbManager.GetConnection()))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    originalSqlMode = result?.ToString() ?? string.Empty;
                }

                var relaxedMode = string.Join(",",
                    originalSqlMode.Split(',')
                        .Where(m => m != "NO_ZERO_DATE" && m != "NO_ZERO_IN_DATE" && m.Length > 0));

                await ExecuteQueryAsync(dbManager, $"SET SESSION sql_mode = '{relaxedMode}';");

                await ExecuteQueryAsync(dbManager, @"
                    UPDATE `tblproduzione`
                    SET `TimeFine` = NULL
                    WHERE CAST(`TimeFine` AS CHAR(19)) = '0000-00-00 00:00:00';");

                await ExecuteQueryAsync(dbManager, @"
                    ALTER TABLE `tblproduzione`
                        MODIFY COLUMN `TimeFine` DATETIME NULL DEFAULT NULL;");
            }
            catch (Exception ex)
            {
                logger?.Warn($"Sanitizzazione TimeFine parziale o fallita; startup continua: {ex.Message}");
            }
            finally
            {
                if (originalSqlMode != null)
                {
                    try
                    {
                        await ExecuteQueryAsync(dbManager, $"SET SESSION sql_mode = '{originalSqlMode}';");
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn($"Impossibile ripristinare il sql_mode originale dopo la migrazione di TimeFine: {ex.Message}");
                    }
                }
            }
        }
        // Fondazione dati AI (Fase 0): misure strutturate di ispezione.
        // Una riga per (prodotto, camera, feature) con valore misurato e tolleranza.
        private async Task CreateInspectionMeasurementsTable(DbConnectionManager dbManager)
        {
            const string query = @"
            CREATE TABLE IF NOT EXISTS `tbl_inspection_measurements` (
                `Id` bigint(20) NOT NULL AUTO_INCREMENT,
                `Timestamp` DATETIME(3) NOT NULL,
                `ProductId` bigint(20) NOT NULL DEFAULT 0,
                `Recipe` varchar(150) DEFAULT NULL,
                `CameraRole` varchar(50) DEFAULT NULL,
                `Feature` varchar(80) DEFAULT NULL,
                `MeasuredValue` DOUBLE NULL DEFAULT NULL,
                `Tolerance` DOUBLE NULL DEFAULT NULL,
                `IsDefect` TINYINT(1) NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`),
                KEY `idx_meas_ts` (`Timestamp`),
                KEY `idx_meas_recipe_feature` (`Recipe`, `Feature`),
                KEY `idx_meas_product` (`ProductId`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            await ExecuteQueryAsync(dbManager, query);
        }

        // Fondazione dati AI (Fase 0): serie storica salute PC per manutenzione predittiva.
        private async Task CreateHealthSnapshotsTable(DbConnectionManager dbManager)
        {
            const string query = @"
            CREATE TABLE IF NOT EXISTS `tbl_health_snapshots` (
                `Id` bigint(20) NOT NULL AUTO_INCREMENT,
                `Timestamp` DATETIME(3) NOT NULL,
                `CpuPercent` DOUBLE NULL DEFAULT NULL,
                `MemoryPercent` DOUBLE NULL DEFAULT NULL,
                `MemoryUsedGb` DOUBLE NULL DEFAULT NULL,
                `MemoryTotalGb` DOUBLE NULL DEFAULT NULL,
                `DiskMaxUsedPercent` DOUBLE NULL DEFAULT NULL,
                PRIMARY KEY (`Id`),
                KEY `idx_health_ts` (`Timestamp`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            await ExecuteQueryAsync(dbManager, query);
        }

        // Integrazione AI (Fase 3a): indice dei campioni etichettati per il training vision.
        private async Task CreateTrainingSamplesTable(DbConnectionManager dbManager)
        {
            const string query = @"
            CREATE TABLE IF NOT EXISTS `tbl_training_samples` (
                `Id` bigint(20) NOT NULL AUTO_INCREMENT,
                `Timestamp` DATETIME(3) NOT NULL,
                `ProductId` bigint(20) NOT NULL DEFAULT 0,
                `Recipe` varchar(150) DEFAULT NULL,
                `Label` varchar(8) DEFAULT NULL,
                `Defects` varchar(500) DEFAULT NULL,
                `PieceFolder` varchar(400) DEFAULT NULL,
                PRIMARY KEY (`Id`),
                KEY `idx_train_ts` (`Timestamp`),
                KEY `idx_train_recipe_label` (`Recipe`, `Label`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            await ExecuteQueryAsync(dbManager, query);
        }

        /// <summary>
        /// Aggiunge una colonna solo se manca. Un fallimento non deve impedire l'avvio della
        /// macchina: la colonna assente degrada la sola funzione che la usa, mentre
        /// un'eccezione qui bloccherebbe l'inizializzazione del database per intero.
        /// </summary>
        private async Task EnsureColumnAsync(
            DbConnectionManager dbManager,
            string tableName,
            string columnName,
            string alterStatement)
        {
            try
            {
                const string probe = @"SELECT COUNT(*) FROM information_schema.COLUMNS
                                       WHERE TABLE_SCHEMA = DATABASE()
                                         AND TABLE_NAME = @TableName
                                         AND COLUMN_NAME = @ColumnName";

                long existing;
                using (var cmd = new MySqlCommand(probe, dbManager.GetConnection()))
                {
                    cmd.CommandTimeout = 60;
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    cmd.Parameters.AddWithValue("@ColumnName", columnName);
                    object scalar = await cmd.ExecuteScalarAsync();
                    existing = scalar == null || scalar == DBNull.Value
                        ? 0
                        : Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
                }

                if (existing > 0)
                {
                    return;
                }

                await ExecuteQueryAsync(dbManager, alterStatement);
            }
            catch (Exception exception)
            {
                logger?.Error(
                    exception,
                    $"DB_ENSURE_COLUMN_FAILED|table={tableName}|column={columnName}");
            }
        }

        private async Task ExecuteQueryAsync(DbConnectionManager dbManager, string query)
        {
            using (var cmd = new MySqlCommand(query, dbManager.GetConnection()))
            {
                // Fase 0 stabilita': limite esplicito, l'avvio non deve restare appeso su un DB lento.
                cmd.CommandTimeout = 60;
                await cmd.ExecuteNonQueryAsync();
            }
        }
        private async Task CreateAlarmEventsTableAsync(DbConnectionManager dbManager)
        {
            const string query = @"
            CREATE TABLE IF NOT EXISTS `alarm_events` (
                `Id`            INT NOT NULL AUTO_INCREMENT,
                `AlarmName`     VARCHAR(100) NOT NULL,
                `TriggerTime`   DATETIME NOT NULL,
                `DefectsJson`   TEXT NULL,
                `OutputChannel` VARCHAR(20) NULL,
                `AcknowledgedBy`  VARCHAR(100) NULL,
                `AcknowledgedAt`  DATETIME NULL,
                `DurationMs`    INT NULL,
                PRIMARY KEY (`Id`),
                KEY `idx_alarm_time` (`TriggerTime`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            await ExecuteQueryAsync(dbManager, query);
        }

        private async Task CreateAuditLogTableAsync(DbConnectionManager dbManager)
        {
            const string query = @"
            CREATE TABLE IF NOT EXISTS `audit_log` (
                `Id`        BIGINT NOT NULL AUTO_INCREMENT,
                `Timestamp` DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
                `EventType` VARCHAR(64) NOT NULL,
                `Actor`     VARCHAR(100) NOT NULL,
                `Details`   TEXT NULL,
                `OldValue`  TEXT NULL,
                `NewValue`  TEXT NULL,
                PRIMARY KEY (`Id`),
                KEY `idx_audit_timestamp` (`Timestamp`),
                KEY `idx_audit_event`     (`EventType`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;";

            await ExecuteQueryAsync(dbManager, query);
            logger?.Info("AUDIT_LOG_TABLE_OK");
        }

        private async Task CreateRetentionEventAsync(DbConnectionManager dbManager)
        {
            try
            {
                const string sql = @"
                    CREATE EVENT IF NOT EXISTS `qtis_archive_old_inspections`
                    ON SCHEDULE EVERY 1 MONTH
                    STARTS (TIMESTAMP(DATE_FORMAT(NOW(), '%Y-%m-01')) + INTERVAL 1 MONTH + INTERVAL 2 HOUR)
                    DO
                    DELETE FROM tblgenerale WHERE DataeOra < DATE_SUB(NOW(), INTERVAL 6 MONTH)";
                await ExecuteQueryAsync(dbManager, sql);
                logger?.Info("RETENTION_EVENT_OK|event=qtis_archive_old_inspections|retention=6_months");
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "RETENTION_EVENT_SKIPPED — MySQL event_scheduler may not be enabled; add event_scheduler=ON to my.cnf");
            }
        }

        private async Task CheckTableSizeAsync(DbConnectionManager dbManager)
        {
            try
            {
                const string sql = @"
                    SELECT IFNULL(TABLE_ROWS, 0)
                    FROM information_schema.TABLES
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'tblgenerale'";
                using (var cmd = new MySqlCommand(sql, dbManager.GetConnection()))
                {
                    long rows = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
                    if (rows > 10_000_000)
                        logger?.Warn("TABLE_SIZE_WARNING|tblgenerale≈{0}_rows — consider manual archival or verify retention event is active", rows);
                    else
                        logger?.Info("TABLE_SIZE_OK|tblgenerale≈{0}_rows", rows);
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "TABLE_SIZE_CHECK_FAILED");
            }
        }

        /// <summary>
        /// Matrice esplicita vista camera x ispezione.
        ///
        /// Tabella separata da `cfg_inspection` di proposito: quella ha UNIQUE KEY
        /// (Feature, MachineType) e viene creata con CREATE TABLE IF NOT EXISTS, quindi gli
        /// impianti gia' installati non riceverebbero mai una nuova colonna. Una tabella
        /// dedicata si crea sui vecchi impianti senza alcuna migrazione e senza toccare la
        /// chiave univoca esistente.
        ///
        /// L'assenza di righe significa "nessun override": il runtime usa la tabella di
        /// applicabilita' storica in CameraInspectionMatrix.DefaultAppliesTo, quindi il
        /// comportamento in campo non cambia finche' l'operatore non salva dalla pagina
        /// Inspection Configuration. Nessun seed: seminare celle a false disabiliterebbe
        /// silenziosamente ispezioni oggi attive (es. BottomSealing e TrappedPaper, che di
        /// default sono true).
        /// </summary>
        private async Task CreateCfgInspectionViewTable(DbConnectionManager dbManager)
        {
            string createCfgInspectionViewTable = @"
CREATE TABLE IF NOT EXISTS `cfg_inspection_view` (
    `Id` bigint(10) NOT NULL AUTO_INCREMENT,
    `Feature` varchar(100) NOT NULL,
    `CameraRole` varchar(50) NOT NULL,
    `MachineType` varchar(50) NOT NULL DEFAULT 'Packs',
    `IsEnabled` BOOLEAN DEFAULT true,
    `LastUpdated` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `uk_Feature_Role_MachineType` (`Feature`, `CameraRole`, `MachineType`),
    KEY `idx_Role_MachineType` (`CameraRole`, `MachineType`)
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4;";

            await ExecuteQueryAsync(dbManager, createCfgInspectionViewTable);
            await CreateCfgViewClassificationTable(dbManager);
        }

        /// <summary>
        /// Classi Classify (ViDi EL) accettate per vista camera.
        ///
        /// La tassonomia di un modello Edge Learning e' addestrata per camera, quindi le
        /// etichette di OK cambiano da vista a vista. Senza righe qui vale il comportamento
        /// storico (lista fissa OK/GOOD/PASS/PASSED/COMPLIANT), quindi la tabella e'
        /// additiva e non cambia nulla finche' non viene configurata.
        /// </summary>
        private async Task CreateCfgViewClassificationTable(DbConnectionManager dbManager)
        {
            string createTable = @"
CREATE TABLE IF NOT EXISTS `cfg_view_classification` (
    `Id` bigint(10) NOT NULL AUTO_INCREMENT,
    `CameraRole` varchar(50) NOT NULL,
    `MachineType` varchar(50) NOT NULL DEFAULT 'Packs',
    `AcceptedClasses` TEXT NULL,
    `MinimumScore` DOUBLE NOT NULL DEFAULT 0,
    `LastUpdated` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `uk_Role_MachineType_Class` (`CameraRole`, `MachineType`)
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4;";

            await ExecuteQueryAsync(dbManager, createTable);

            // Migrazione per le installazioni che hanno gia' la tabella: CREATE TABLE IF NOT
            // EXISTS non aggiunge colonne a una tabella esistente.
            //
            // Il controllo si fa in C# e non in SQL: la versione con SET @ddl + PREPARE
            // richiederebbe AllowUserVariables nella stringa di connessione, che qui non e'
            // impostata (solo MySqlPieceHistoryRepository la usa), e fallirebbe bloccando
            // l'avvio. ADD COLUMN IF NOT EXISTS non e' un'opzione: MySQL 5.7 non lo supporta.
            await EnsureColumnAsync(
                dbManager,
                "cfg_view_classification",
                "MinimumScore",
                "ALTER TABLE `cfg_view_classification` " +
                "ADD COLUMN `MinimumScore` DOUBLE NOT NULL DEFAULT 0 AFTER `AcceptedClasses`");
        }

        private async Task CreateCfgInspectionTable(DbConnectionManager dbManager)
        {
            await CreateCfgInspectionViewTable(dbManager);

            string createCfgInspectionTable = @"
CREATE TABLE IF NOT EXISTS `cfg_inspection` (
    `Id` bigint(10) NOT NULL AUTO_INCREMENT,
    `Feature` varchar(100) NOT NULL,
    `Description` varchar(255) DEFAULT NULL,
    `IsEnabled` BOOLEAN DEFAULT true,
    `MachineType` varchar(50) DEFAULT 'Packs',
    `LastUpdated` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `uk_Feature_MachineType` (`Feature`, `MachineType`),
    KEY `idx_MachineType` (`MachineType`)
) ENGINE=InnoDB AUTO_INCREMENT=1 DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO `cfg_inspection` (Feature, Description, IsEnabled, MachineType) VALUES
('Logo', 'Logo/stampa', true, 'Packs'),
('ShapeTop', 'Forma superiore', true, 'Packs'),
('OpenFlaps', 'Alette aperte', true, 'Packs'),
('SurfaceCheck', 'Controllo superficie', true, 'Packs'),
('PrintCentering', 'Centratura stampa', true, 'Packs'),
('SideSealing', 'Sigillatura laterale', true, 'Packs'),
('SideRollCount', 'Conteggio rotoli camera left', true, 'Packs'),
('ShapeSide', 'Forma laterale', true, 'Packs'),
('Height', 'Altezza', true, 'Packs'),
('FrontTraceability', 'Tracciabilita front', true, 'Packs'),
('BottomSealing', 'Saldatura inferiore', true, 'Packs'),
('TrappedPaper', 'Carta intrappolata nella saldatura', true, 'Packs'),
('AIClassification', 'Classificazione AI VisionPro', false, 'Packs'),
-- Per Rolls
('Lenght', 'Lunghezza', false, 'Rolls'),
('Diameter', 'Diametro', false, 'Rolls'),
('DirtyOnTop', 'sporco Vista superiore', false, 'Rolls'),
('DeformedCodeFront', 'anaima sbeccata front', false, 'Rolls'),
('DirtyFront', 'Sporco vista front', false, 'Rolls'),
('DeformedCoreRear', 'anaima sbeccata rear', false, 'Rolls'),
('DirtyRear', 'sporco vista rear', false, 'Rolls'),
('TippedRoll', 'rotolo Girato', false, 'Rolls'),
('FrontTraceability', 'Tracciabilita front', false, 'Rolls'),
('AIClassification', 'Classificazione AI VisionPro', false, 'Rolls'),
-- Per Bags
('Logo', 'Logo/stampa', false, 'Bags'),
('ShapeTop', 'Forma superiore', false, 'Bags'),
('OpenFlaps', 'Alette aperte', false, 'Bags'),
('SurfaceCheck', 'Controllo superficie', false, 'Bags'),
('PrintCentering', 'Centratura stampa', false, 'Bags'),
('SideSealing', 'Sigillatura laterale', false, 'Bags'),
('SideRollCount', 'Conteggio rotoli camera left', false, 'Bags'),
('ShapeSide', 'Forma laterale', false, 'Bags'),
('Height', 'Altezza', false, 'Bags'),
('FrontTraceability', 'Tracciabilita front', false, 'Bags'),
('BottomSealing', 'Saldatura inferiore', false, 'Bags'),
('TrappedPaper', 'Carta intrappolata nella saldatura', false, 'Bags'),
('AIClassification', 'Classificazione AI VisionPro', false, 'Bags'),
('AIClassification', 'Classificazione AI VisionPro', false, '3DCheck');";

            await ExecuteQueryAsync(dbManager, createCfgInspectionTable);
        }

    }

    public class InserdataInDb
    {

        private static string NormalizeRecipeDatabaseName(string recipeName)
        {
            string fileName = Path.GetFileName((recipeName ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            return fileName.EndsWith(".vpp", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : fileName + ".vpp";
        }

        public async Task<long?> GetRicettaIdByNameAsync(string nomeRicetta)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string query = "SELECT IdProduzione FROM tblproduzione WHERE Ricetta = @Ricetta AND IsDeleted = FALSE LIMIT 1";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Ricetta", NormalizeRecipeDatabaseName(nomeRicetta));

                        var result = await cmd.ExecuteScalarAsync();
                        return result != null ? Convert.ToInt64(result) : (long?)null;
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to get ricetta ID: " + ex.Message + "\n" + ex.StackTrace);
                return null;
            }
        }

        public async Task<string> GetRicettaNameByIdAsync(long idProduzione)
        {
            if (idProduzione <= 0)
            {
                return null;
            }

            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    const string query = @"
                        SELECT Ricetta
                        FROM tblproduzione
                        WHERE IdProduzione = @IdProduzione
                          AND IsDeleted = FALSE
                        LIMIT 1";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@IdProduzione", idProduzione);

                        var result = await cmd.ExecuteScalarAsync();
                        return result != null && result != DBNull.Value
                            ? Convert.ToString(result)
                            : null;
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to get ricetta name by ID: " + ex.Message + "\n" + ex.StackTrace);
                return null;
            }
        }
        public async Task SaveCountersAsync(Dictionary<string, long> counters)
        {
            if (counters == null || counters.Count == 0)
            {
                MainWindow.logger?.Debug("Nessun contatore da salvare");
                return;
            }

            bool dbAvailable = await CheckDatabaseConnectionAsync();
            if (!dbAvailable)
            {
                MainWindow.logger?.Warn("Database non raggiungibile, salto salvataggio");
                return;
            }

            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await Task.Run(async () =>
                    {
                        await SaveCountersToDatabaseInternalAsync(counters, timeoutCts.Token);
                    }, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                MainWindow.logger?.Warn("Salvataggio contatori cancellato per timeout");
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nel salvataggio dei contatori: {ex.Message}");
            }
        }

        private async Task SaveCountersToDatabaseInternalAsync(Dictionary<string, long> counters, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = DbConnectionStringHelper.Build(
                config.Host, config.Db, config.User, config.Password, config.port);

            var featureMapping = new Dictionary<string, string>
            {
                { "Total", "TOTAL" }, { "Good", "GOOD" }, { "NoGood", "NOGOOD" },
                { "Unclassified", "UNCLASSIFIED" },
                { "Logo", "LOGO" }, { "PrintCentering", "PRINT_CENTERING" },
                { "OpenFlaps", "OPEN_FLAPS" }, { "SurfaceCheck", "SURFACE_CHECK" },
                { "Height", "HEIGHT" }, { "SideSealing", "SIDE_SEALING" },
                { "ShapeTop", "SHAPE_TOP" }, { "ShapeSide", "SHAPE_SIDE" },
                { "FrontTraceability", "TRACEABILITY" },
                { "ThreeDHeight", "THREED_HEIGHT" },
                { "ThreeDWidth", "THREED_WIDTH" },
                { "ThreeDLength", "THREED_LENGTH" },
                { "BottomSealing", "BOTTOM_SEALING" },
                { "TrappedPaper", "TRAPPED_PAPER" },
                { "AIClassification", "AI_CLASSIFICATION" }
            };

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync(token);

                    var batchQuery = new StringBuilder();
                    var parameters = new List<MySqlParameter>();
                    int paramIndex = 0;
                    var now = DateTime.Now;

                    foreach (var counter in counters)
                    {
                        if (!featureMapping.TryGetValue(counter.Key, out string featureName))
                            continue;

                        batchQuery.AppendLine(
                            "INSERT INTO tblglobalcounters (Feature, Counter, LastUpdated) " +
                            $"VALUES (@feature{paramIndex}, @counter{paramIndex}, @lastUpdated{paramIndex}) " +
                            "ON DUPLICATE KEY UPDATE Counter = VALUES(Counter), LastUpdated = VALUES(LastUpdated);");

                        parameters.Add(new MySqlParameter($"@feature{paramIndex}", featureName));
                        parameters.Add(new MySqlParameter($"@counter{paramIndex}", counter.Value));
                        parameters.Add(new MySqlParameter($"@lastUpdated{paramIndex}", now));
                        paramIndex++;
                    }

                    if (parameters.Count == 0) return;

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            using (var command = new MySqlCommand(batchQuery.ToString(), connection, transaction))
                            {
                                command.Parameters.AddRange(parameters.ToArray());
                                await command.ExecuteNonQueryAsync(token);
                            }
                            transaction.Commit();
                            MainWindow.logger?.Debug($"Salvati {counters.Count} contatori in transazione");
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (MySqlException mysqlEx) when (mysqlEx.Number == 1146)
            {
                MainWindow.logger?.Warn("Tabella tblglobalcounters non trovata. Creazione...");
                await CreateCountersTableIfNotExistsAsync();
                await SaveCountersToDatabaseInternalAsync(counters, token);
            }
            catch (MySqlException mysqlEx)
            {
                MainWindow.logger?.Warn($"Errore MySQL non critico: {mysqlEx.Message}");
            }
        }
        private async Task CreateCountersTableIfNotExistsAsync()
        {
            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port};Connection Timeout=5";

                using (var connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    string createTableQuery = @"
            CREATE TABLE IF NOT EXISTS `tblglobalcounters` (
                `Id` bigint(10) NOT NULL AUTO_INCREMENT,
                `Feature` varchar(100) NOT NULL,
                `Counter` BIGINT NOT NULL DEFAULT 0,
                `Description` varchar(255) DEFAULT NULL,
                `LastUpdated` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (`Id`),
                UNIQUE KEY `uk_Feature` (`Feature`),
                KEY `idx_LastUpdated` (`LastUpdated`)
            ) ENGINE=InnoDB AUTO_INCREMENT=1;
            ALTER TABLE tblglobalcounters MODIFY COLUMN Counter BIGINT NOT NULL DEFAULT 0;

            INSERT IGNORE INTO `tblglobalcounters` (Feature, Description, Counter) VALUES
                ('TOTAL', 'Totale ispezioni', 0),
                ('GOOD', 'Prodotti conformi', 0),
                ('NOGOOD', 'Prodotti non conformi', 0),
                ('UNCLASSIFIED', 'Pezzi non classificati per errore elaborazione visione', 0),
                ('LOGO', 'Difetti logo/stampa', 0),
                ('SHAPE_TOP', 'Difetti forma superiore', 0),
                ('OPEN_FLAPS', 'Difetti alette aperte', 0),
                ('SURFACE_CHECK', 'Difetti controllo superficie', 0),
                ('PRINT_CENTERING', 'Difetti centratura stampa', 0),
                ('SIDE_SEALING', 'Difetti sigillatura laterale', 0),
                ('SHAPE_SIDE', 'Difetti forma laterale', 0),
                ('HEIGHT', 'Difetti altezza', 0),
                ('TRACEABILITY', 'Difetti tracciabilita front', 0);";

                    using (var command = new MySqlCommand(createTableQuery, connection))
                        await command.ExecuteNonQueryAsync();

                    MainWindow.logger?.Info("Tabella tblglobalcounters creata/verificata");
                }
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Error($"Errore nella creazione della tabella: {ex.Message}");
            }
        }
        private async Task<bool> CheckDatabaseConnectionAsync()
        {
            try
            {
                var config = MainWindow.configManager.Config.MySqlConnection;
                string testConnectionString = $"Server={config.Host};Uid={config.User};Pwd={config.Password};Port={config.port};Connection Timeout=10";

                using (var connection = new MySqlConnection(testConnectionString))
                {
                    await connection.OpenAsync();
                    await connection.CloseAsync();
                    return true;
                } 
               
            }
            catch (Exception ex)
            {
                MainWindow.logger?.Warn($"Database non raggiungibile: {ex.Message}");
                return false;
            }
        }

        private static bool ColumnExists(MySqlConnection conn, string databaseName, string tableName, string columnName)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @schema
                  AND TABLE_NAME = @table
                  AND COLUMN_NAME = @column;";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@schema", databaseName);
                cmd.Parameters.AddWithValue("@table", tableName);
                cmd.Parameters.AddWithValue("@column", columnName);

                var result = cmd.ExecuteScalar();
                return Convert.ToInt32(result) > 0;
            }
        }

        private static HashSet<string> GetExistingColumns(
            MySqlConnection conn,
            string databaseName,
            string tableName)
        {
            const string sql = @"
                SELECT COLUMN_NAME
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @schema
                  AND TABLE_NAME = @table";

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@schema", databaseName);
                cmd.Parameters.AddWithValue("@table", tableName);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
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

        public void InsertDataConforme(ProduzioneRecord record)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                HashSet<string> existingColumns = GetExistingColumns(conn, config.Db, "tblgenerale");
                bool hasInspectionStatusDetails = existingColumns.Contains("InspectionStatusDetails");
                bool hasTopClassificationLabel = existingColumns.Contains("TopClassificationLabel");
                bool hasTopClassificationScore = existingColumns.Contains("TopClassificationScore");
                bool hasSideClassificationLabel = existingColumns.Contains("SideClassificationLabel");
                bool hasSideClassificationScore = existingColumns.Contains("SideClassificationScore");
                bool hasFrontClassificationLabel = existingColumns.Contains("FrontClassificationLabel");
                bool hasFrontClassificationScore = existingColumns.Contains("FrontClassificationScore");
                bool hasRearClassificationLabel = existingColumns.Contains("RearClassificationLabel");
                bool hasRearClassificationScore = existingColumns.Contains("RearClassificationScore");
                bool hasRightClassificationLabel = existingColumns.Contains("RightClassificationLabel");
                bool hasRightClassificationScore = existingColumns.Contains("RightClassificationScore");
                bool hasBottomClassificationLabel = existingColumns.Contains("BottomClassificationLabel");
                bool hasBottomClassificationScore = existingColumns.Contains("BottomClassificationScore");
                bool hasNcTopAiClassification = existingColumns.Contains("NC_TopAiClassification");
                bool hasNcSideAiClassification = existingColumns.Contains("NC_SideAiClassification");
                bool hasNcFrontAiClassification = existingColumns.Contains("NC_FrontAiClassification");
                bool hasNcRearAiClassification = existingColumns.Contains("NC_RearAiClassification");
                bool hasNcRightAiClassification = existingColumns.Contains("NC_RightAiClassification");
                bool hasNcBottomAiClassification = existingColumns.Contains("NC_BottomAiClassification");
                bool hasTraceabilityDetected = existingColumns.Contains("TraceabilityDetected");
                bool hasTraceabilityCode = existingColumns.Contains("TraceabilityCode");
                bool hasTraceabilityExpectedPrefix = existingColumns.Contains("TraceabilityExpectedPrefix");
                bool hasNcTraceability = existingColumns.Contains("NC_Traceability");
                bool hasThreeDHeightNominal = existingColumns.Contains("ThreeDHeightNominalValue");
                bool hasThreeDHeightMeasured = existingColumns.Contains("ThreeDHeightMeasureValue");
                bool hasThreeDHeightMinimum = existingColumns.Contains("ThreeDHeight_Minimum");
                bool hasThreeDHeightMaximum = existingColumns.Contains("ThreeDHeight_Maximum");
                bool hasNcThreeDHeight = existingColumns.Contains("NC_ThreeDHeight");
                bool hasThreeDHeightMedian = existingColumns.Contains("ThreeDHeightMedianValue");
                bool hasThreeDHeightHighTail = existingColumns.Contains("ThreeDHeightHighTailValue");
                bool hasThreeDHeightDiagnosticMaximum = existingColumns.Contains("ThreeDHeightMaximumValue");
                bool hasThreeDHeightBulge = existingColumns.Contains("ThreeDHeightBulgeValue");
                bool hasThreeDHeightValidPixelRatio = existingColumns.Contains("ThreeDHeightValidPixelRatio");
                bool hasNcThreeDProfile = existingColumns.Contains("NCThreeDProfile");
                bool hasThreeDWidthNominal = existingColumns.Contains("ThreeDWidthNominalValue");
                bool hasThreeDWidthMeasured = existingColumns.Contains("ThreeDWidthMeasureValue");
                bool hasThreeDWidthMinimum = existingColumns.Contains("ThreeDWidth_Minimum");
                bool hasThreeDWidthMaximum = existingColumns.Contains("ThreeDWidth_Maximum");
                bool hasNcThreeDWidth = existingColumns.Contains("NC_ThreeDWidth");
                bool hasThreeDLengthNominal = existingColumns.Contains("ThreeDLengthNominalValue");
                bool hasThreeDLengthMeasured = existingColumns.Contains("ThreeDLengthMeasureValue");
                bool hasThreeDLengthMinimum = existingColumns.Contains("ThreeDLength_Minimum");
                bool hasThreeDLengthMaximum = existingColumns.Contains("ThreeDLength_Maximum");
                bool hasNcThreeDLength = existingColumns.Contains("NC_ThreeDLength");
                bool hasNcBottomSealing = existingColumns.Contains("NC_BottomSealing");
                bool hasNcTrappedPaper = existingColumns.Contains("NC_TrappedPaper");

                var columns = new List<string>
                {
                    "IdProduzione",
                    "DataeOra",
                    "Operatore",
                    "Prodotto",
                    "Ricetta",
                    "Esito_Classificazione",
                    "MinValueLogoToll",
                    "LogoMatchPerc",
                    "NC_Loghi_Immagini",
                    "TopValueToMarker",
                    "LeftValueToMarker",
                    "RigthValueToMarker",
                    "BottomValueToMarker",
                    "NC_Centratura_Logo",
                    "Area1FlapsDetected",
                    "Area2FlapsDetected",
                    "NC_Alette_Aperte",
                    "HeigthNominalValue",
                    "HeigthMeasureValue",
                    "Heigth_Minimum",
                    "Heigth_Maximum",
                    "NC_Heigth",
                    "MinAreaSealing",
                    "AreaSealing",
                    "NC_Saldatura_Laterale",
                    "A_T_Value",
                    "A_T_Toll",
                    "A_T_topLeft",
                    "A_T_TopRigth",
                    "A_T_bottomLeft",
                    "A_T_BottomRigth",
                    "NC_ShapeTop",
                    "A_B_Value",
                    "A_B_Toll",
                    "A_B_topLeft",
                    "A_B_TopRigth",
                    "A_B_bottomLeft",
                    "A_B_BottomRigth",
                    "NC_ShapeBottom",
                    "Espulsione_Comandata",
                    "DataHostnames",
                    "PieceData",
                    "NC_SurfaceCheck"
                };

                var values = new List<string>
                {
                    "@IdProduzione",
                    "@DataeOra",
                    "@Operatore",
                    "@Prodotto",
                    "@Ricetta",
                    "@Esito_Classificazione",
                    "@MinValueLogoToll",
                    "@LogoMatchPerc",
                    "@NC_Loghi_Immagini",
                    "@TopValueToMarker",
                    "@LeftValueToMarker",
                    "@RigthValueToMarker",
                    "@BottomValueToMarker",
                    "@NC_Centratura_Logo",
                    "@Area1FlapsDetected",
                    "@Area2FlapsDetected",
                    "@NC_Alette_Aperte",
                    "@HeigthNominalValue",
                    "@HeigthMeasureValue",
                    "@Heigth_Minimum",
                    "@Heigth_Maximum",
                    "@NC_Heigth",
                    "@MinAreaSealing",
                    "@AreaSealing",
                    "@NC_Saldatura_Laterale",
                    "@A_T_Value",
                    "@A_T_Toll",
                    "@A_T_topLeft",
                    "@A_T_TopRigth",
                    "@A_T_bottomLeft",
                    "@A_T_BottomRigth",
                    "@NC_ShapeTop",
                    "@A_B_Value",
                    "@A_B_Toll",
                    "@A_B_topLeft",
                    "@A_B_TopRigth",
                    "@A_B_bottomLeft",
                    "@A_B_BottomRigth",
                    "@NC_ShapeBottom",
                    "@Espulsione_Comandata",
                    "@DataHostnames",
                    "@PieceData",
                    "@NC_SurfaceCheck"
                };

                if (hasInspectionStatusDetails)
                {
                    columns.Add("InspectionStatusDetails");
                    values.Add("@InspectionStatusDetails");
                }

                if (hasTopClassificationLabel)
                {
                    columns.Add("TopClassificationLabel");
                    values.Add("@TopClassificationLabel");
                }

                if (hasTopClassificationScore)
                {
                    columns.Add("TopClassificationScore");
                    values.Add("@TopClassificationScore");
                }

                if (hasSideClassificationLabel)
                {
                    columns.Add("SideClassificationLabel");
                    values.Add("@SideClassificationLabel");
                }

                if (hasSideClassificationScore)
                {
                    columns.Add("SideClassificationScore");
                    values.Add("@SideClassificationScore");
                }

                if (hasFrontClassificationLabel)
                {
                    columns.Add("FrontClassificationLabel");
                    values.Add("@FrontClassificationLabel");
                }

                if (hasFrontClassificationScore)
                {
                    columns.Add("FrontClassificationScore");
                    values.Add("@FrontClassificationScore");
                }

                if (hasRearClassificationLabel)
                {
                    columns.Add("RearClassificationLabel");
                    values.Add("@RearClassificationLabel");
                }

                if (hasRearClassificationScore)
                {
                    columns.Add("RearClassificationScore");
                    values.Add("@RearClassificationScore");
                }

                if (hasRightClassificationLabel)
                {
                    columns.Add("RightClassificationLabel");
                    values.Add("@RightClassificationLabel");
                }
                if (hasRightClassificationScore)
                {
                    columns.Add("RightClassificationScore");
                    values.Add("@RightClassificationScore");
                }

                if (hasBottomClassificationLabel)
                {
                    columns.Add("BottomClassificationLabel");
                    values.Add("@BottomClassificationLabel");
                }

                if (hasBottomClassificationScore)
                {
                    columns.Add("BottomClassificationScore");
                    values.Add("@BottomClassificationScore");
                }

                if (hasNcTopAiClassification)
                {
                    columns.Add("NC_TopAiClassification");
                    values.Add("@NC_TopAiClassification");
                }

                if (hasNcSideAiClassification)
                {
                    columns.Add("NC_SideAiClassification");
                    values.Add("@NC_SideAiClassification");
                }

                if (hasNcFrontAiClassification)
                {
                    columns.Add("NC_FrontAiClassification");
                    values.Add("@NC_FrontAiClassification");
                }

                if (hasNcRearAiClassification)
                {
                    columns.Add("NC_RearAiClassification");
                    values.Add("@NC_RearAiClassification");
                }
                if (hasNcRightAiClassification)
                {
                    columns.Add("NC_RightAiClassification");
                    values.Add("@NC_RightAiClassification");
                }

                if (hasNcBottomAiClassification)
                {
                    columns.Add("NC_BottomAiClassification");
                    values.Add("@NC_BottomAiClassification");
                }

                if (hasTraceabilityDetected)
                {
                    columns.Add("TraceabilityDetected");
                    values.Add("@TraceabilityDetected");
                }

                if (hasTraceabilityCode)
                {
                    columns.Add("TraceabilityCode");
                    values.Add("@TraceabilityCode");
                }

                if (hasTraceabilityExpectedPrefix)
                {
                    columns.Add("TraceabilityExpectedPrefix");
                    values.Add("@TraceabilityExpectedPrefix");
                }

                if (hasNcTraceability)
                {
                    columns.Add("NC_Traceability");
                    values.Add("@NC_Traceability");
                }

                if (hasThreeDHeightNominal)
                {
                    columns.Add("ThreeDHeightNominalValue");
                    values.Add("@ThreeDHeightNominalValue");
                }

                if (hasThreeDHeightMeasured)
                {
                    columns.Add("ThreeDHeightMeasureValue");
                    values.Add("@ThreeDHeightMeasureValue");
                }

                if (hasThreeDHeightMinimum)
                {
                    columns.Add("ThreeDHeight_Minimum");
                    values.Add("@ThreeDHeight_Minimum");
                }

                if (hasThreeDHeightMaximum)
                {
                    columns.Add("ThreeDHeight_Maximum");
                    values.Add("@ThreeDHeight_Maximum");
                }

                if (hasNcThreeDHeight)
                {
                    columns.Add("NC_ThreeDHeight");
                    values.Add("@NC_ThreeDHeight");
                }

                if (hasThreeDHeightMedian)
                {
                    columns.Add("ThreeDHeightMedianValue");
                    values.Add("@ThreeDHeightMedianValue");
                }

                if (hasThreeDHeightHighTail)
                {
                    columns.Add("ThreeDHeightHighTailValue");
                    values.Add("@ThreeDHeightHighTailValue");
                }

                if (hasThreeDHeightDiagnosticMaximum)
                {
                    columns.Add("ThreeDHeightMaximumValue");
                    values.Add("@ThreeDHeightMaximumValue");
                }

                if (hasThreeDHeightBulge)
                {
                    columns.Add("ThreeDHeightBulgeValue");
                    values.Add("@ThreeDHeightBulgeValue");
                }

                if (hasThreeDHeightValidPixelRatio)
                {
                    columns.Add("ThreeDHeightValidPixelRatio");
                    values.Add("@ThreeDHeightValidPixelRatio");
                }

                if (hasNcThreeDProfile)
                {
                    columns.Add("NCThreeDProfile");
                    values.Add("@NCThreeDProfile");
                }

                if (hasThreeDWidthNominal)
                {
                    columns.Add("ThreeDWidthNominalValue");
                    values.Add("@ThreeDWidthNominalValue");
                }

                if (hasThreeDWidthMeasured)
                {
                    columns.Add("ThreeDWidthMeasureValue");
                    values.Add("@ThreeDWidthMeasureValue");
                }

                if (hasThreeDWidthMinimum)
                {
                    columns.Add("ThreeDWidth_Minimum");
                    values.Add("@ThreeDWidth_Minimum");
                }

                if (hasThreeDWidthMaximum)
                {
                    columns.Add("ThreeDWidth_Maximum");
                    values.Add("@ThreeDWidth_Maximum");
                }

                if (hasNcThreeDWidth)
                {
                    columns.Add("NC_ThreeDWidth");
                    values.Add("@NC_ThreeDWidth");
                }

                if (hasThreeDLengthNominal)
                {
                    columns.Add("ThreeDLengthNominalValue");
                    values.Add("@ThreeDLengthNominalValue");
                }

                if (hasThreeDLengthMeasured)
                {
                    columns.Add("ThreeDLengthMeasureValue");
                    values.Add("@ThreeDLengthMeasureValue");
                }

                if (hasThreeDLengthMinimum)
                {
                    columns.Add("ThreeDLength_Minimum");
                    values.Add("@ThreeDLength_Minimum");
                }

                if (hasThreeDLengthMaximum)
                {
                    columns.Add("ThreeDLength_Maximum");
                    values.Add("@ThreeDLength_Maximum");
                }

                if (hasNcThreeDLength)
                {
                    columns.Add("NC_ThreeDLength");
                    values.Add("@NC_ThreeDLength");
                }

                if (hasNcBottomSealing)
                {
                    columns.Add("NC_BottomSealing");
                    values.Add("@NC_BottomSealing");
                }

                if (hasNcTrappedPaper)
                {
                    columns.Add("NC_TrappedPaper");
                    values.Add("@NC_TrappedPaper");
                }

                string query = $"INSERT INTO tblgenerale ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});";

                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@IdProduzione", record.IdProduzione);
                    cmd.Parameters.AddWithValue("@DataeOra", record.DataEOra);
                    cmd.Parameters.AddWithValue("@Operatore", record.Operatore ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Prodotto", record.Prodotto ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Ricetta", record.Ricetta ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Esito_Classificazione", record.EsitoClassificazione ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@MinValueLogoToll", record.MinValueLogoToll ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LogoMatchPerc", record.LogoMatchPerc ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NC_Loghi_Immagini", record.NcLoghiImmagini ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@TopValueToMarker", record.TopValueToMarker ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LeftValueToMarker", record.LeftValueToMarker ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@RigthValueToMarker", record.RigthValueToMarker ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@BottomValueToMarker", record.BottomValueToMarker ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NC_Centratura_Logo", record.NcCentraturaLogo ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Area1FlapsDetected", record.Area1FlapsDetected ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Area2FlapsDetected", record.Area2FlapsDetected ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NC_Alette_Aperte", record.NcAletteAperte ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@HeigthNominalValue", record.HeigthNominalValue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@HeigthMeasureValue", record.HeigthMeasureValue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Heigth_Minimum", record.HeigthMinimum ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Heigth_Maximum", record.HeigthMaximum ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NC_Heigth", record.NcHeigth ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@MinAreaSealing", record.MinAreaSealing ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@AreaSealing", record.AreaSealing ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NC_Saldatura_Laterale", record.NcSaldaturaLaterale ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_T_Value", record.ATValue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_T_Toll", record.ATToll ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_T_topLeft", record.ATTopLeft ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_T_TopRigth", record.ATTopRigth ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_T_bottomLeft", record.ATBottomLeft ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_T_BottomRigth", record.ATBottomRigth ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NC_ShapeTop", record.NcShapeTop ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_B_Value", record.ABValue ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_B_Toll", record.ABToll ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_B_topLeft", record.ABTopLeft ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_B_TopRigth", record.ABTopRigth ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_B_bottomLeft", record.ABBottomLeft ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@A_B_BottomRigth", record.ABBottomRigth ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NC_ShapeBottom", record.NcShapeBottom ?? (object)DBNull.Value);

                    // da aggiungere i campi nuovi 
                    cmd.Parameters.AddWithValue("@Espulsione_Comandata", record.EspulsioneComandata ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DataHostnames", record.DataHostnames ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@PieceData", record.PieceData ?? (object)DBNull.Value);
                    if (hasInspectionStatusDetails)
                    {
                        cmd.Parameters.AddWithValue("@InspectionStatusDetails", record.InspectionStatusDetails ?? (object)DBNull.Value);
                    }
                    if (hasTopClassificationLabel)
                    {
                        cmd.Parameters.AddWithValue("@TopClassificationLabel", record.TopClassificationLabel ?? (object)DBNull.Value);
                    }
                    if (hasTopClassificationScore)
                    {
                        cmd.Parameters.AddWithValue("@TopClassificationScore", record.TopClassificationScore ?? (object)DBNull.Value);
                    }
                    if (hasSideClassificationLabel)
                    {
                        cmd.Parameters.AddWithValue("@SideClassificationLabel", record.SideClassificationLabel ?? (object)DBNull.Value);
                    }
                    if (hasSideClassificationScore)
                    {
                        cmd.Parameters.AddWithValue("@SideClassificationScore", record.SideClassificationScore ?? (object)DBNull.Value);
                    }
                    if (hasFrontClassificationLabel)
                    {
                        cmd.Parameters.AddWithValue("@FrontClassificationLabel", record.FrontClassificationLabel ?? (object)DBNull.Value);
                    }
                    if (hasFrontClassificationScore)
                    {
                        cmd.Parameters.AddWithValue("@FrontClassificationScore", record.FrontClassificationScore ?? (object)DBNull.Value);
                    }
                    if (hasRearClassificationLabel)
                    {
                        cmd.Parameters.AddWithValue("@RearClassificationLabel", record.RearClassificationLabel ?? (object)DBNull.Value);
                    }
                    if (hasRearClassificationScore)
                    {
                        cmd.Parameters.AddWithValue("@RearClassificationScore", record.RearClassificationScore ?? (object)DBNull.Value);
                    }
                    if (hasRightClassificationLabel)
                    {
                        cmd.Parameters.AddWithValue("@RightClassificationLabel", record.RightClassificationLabel ?? (object)DBNull.Value);
                    }
                    if (hasRightClassificationScore)
                    {
                        cmd.Parameters.AddWithValue("@RightClassificationScore", record.RightClassificationScore ?? (object)DBNull.Value);
                    }
                    if (hasBottomClassificationLabel)
                    {
                        cmd.Parameters.AddWithValue("@BottomClassificationLabel", record.BottomClassificationLabel ?? (object)DBNull.Value);
                    }
                    if (hasBottomClassificationScore)
                    {
                        cmd.Parameters.AddWithValue("@BottomClassificationScore", record.BottomClassificationScore ?? (object)DBNull.Value);
                    }
                    if (hasNcTopAiClassification)
                    {
                        cmd.Parameters.AddWithValue("@NC_TopAiClassification", record.NCTopAiClassification ?? (object)DBNull.Value);
                    }
                    if (hasNcSideAiClassification)
                    {
                        cmd.Parameters.AddWithValue("@NC_SideAiClassification", record.NCSideAiClassification ?? (object)DBNull.Value);
                    }
                    if (hasNcFrontAiClassification)
                    {
                        cmd.Parameters.AddWithValue("@NC_FrontAiClassification", record.NCFrontAiClassification ?? (object)DBNull.Value);
                    }
                    if (hasNcRearAiClassification)
                    {
                        cmd.Parameters.AddWithValue("@NC_RearAiClassification", record.NCRearAiClassification ?? (object)DBNull.Value);
                    }
                    if (hasNcRightAiClassification)
                    {
                        cmd.Parameters.AddWithValue("@NC_RightAiClassification", record.NCRightAiClassification ?? (object)DBNull.Value);
                    }
                    if (hasNcBottomAiClassification)
                    {
                        cmd.Parameters.AddWithValue("@NC_BottomAiClassification", record.NCBottomAiClassification ?? (object)DBNull.Value);
                    }
                    cmd.Parameters.AddWithValue("@NC_SurfaceCheck", record.NCSurfaceCheck ?? (object)DBNull.Value);
                    if (hasTraceabilityDetected)
                    {
                        cmd.Parameters.AddWithValue("@TraceabilityDetected", record.TraceabilityDetected ?? (object)DBNull.Value);
                    }
                    if (hasTraceabilityCode)
                    {
                        cmd.Parameters.AddWithValue("@TraceabilityCode", record.TraceabilityCode ?? (object)DBNull.Value);
                    }
                    if (hasTraceabilityExpectedPrefix)
                    {
                        cmd.Parameters.AddWithValue("@TraceabilityExpectedPrefix", record.TraceabilityExpectedPrefix ?? (object)DBNull.Value);
                    }
                    if (hasNcTraceability)
                    {
                        cmd.Parameters.AddWithValue("@NC_Traceability", record.NCTraceability ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightNominal)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeightNominalValue", record.ThreeDHeightNominalValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightMeasured)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeightMeasureValue", record.ThreeDHeightMeasureValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightMinimum)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeight_Minimum", record.ThreeDHeightMinimum ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightMaximum)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeight_Maximum", record.ThreeDHeightMaximum ?? (object)DBNull.Value);
                    }
                    if (hasNcThreeDHeight)
                    {
                        cmd.Parameters.AddWithValue("@NC_ThreeDHeight", record.NCThreeDHeight ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightMedian)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeightMedianValue", record.ThreeDHeightMedianValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightHighTail)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeightHighTailValue", record.ThreeDHeightHighTailValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightDiagnosticMaximum)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeightMaximumValue", record.ThreeDHeightMaximumValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightBulge)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeightBulgeValue", record.ThreeDHeightBulgeValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDHeightValidPixelRatio)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDHeightValidPixelRatio", record.ThreeDHeightValidPixelRatio ?? (object)DBNull.Value);
                    }
                    if (hasNcThreeDProfile)
                    {
                        cmd.Parameters.AddWithValue("@NCThreeDProfile", record.NCThreeDProfile ?? (object)DBNull.Value);
                    }
                    if (hasThreeDWidthNominal)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDWidthNominalValue", record.ThreeDWidthNominalValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDWidthMeasured)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDWidthMeasureValue", record.ThreeDWidthMeasureValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDWidthMinimum)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDWidth_Minimum", record.ThreeDWidthMinimum ?? (object)DBNull.Value);
                    }
                    if (hasThreeDWidthMaximum)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDWidth_Maximum", record.ThreeDWidthMaximum ?? (object)DBNull.Value);
                    }
                    if (hasNcThreeDWidth)
                    {
                        cmd.Parameters.AddWithValue("@NC_ThreeDWidth", record.NCThreeDWidth ?? (object)DBNull.Value);
                    }
                    if (hasThreeDLengthNominal)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDLengthNominalValue", record.ThreeDLengthNominalValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDLengthMeasured)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDLengthMeasureValue", record.ThreeDLengthMeasureValue ?? (object)DBNull.Value);
                    }
                    if (hasThreeDLengthMinimum)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDLength_Minimum", record.ThreeDLengthMinimum ?? (object)DBNull.Value);
                    }
                    if (hasThreeDLengthMaximum)
                    {
                        cmd.Parameters.AddWithValue("@ThreeDLength_Maximum", record.ThreeDLengthMaximum ?? (object)DBNull.Value);
                    }
                    if (hasNcThreeDLength)
                    {
                        cmd.Parameters.AddWithValue("@NC_ThreeDLength", record.NCThreeDLength ?? (object)DBNull.Value);
                    }
                    if (hasNcBottomSealing)
                    {
                        cmd.Parameters.AddWithValue("@NC_BottomSealing", record.NCBottomSealing ?? (object)DBNull.Value);
                    }
                    if (hasNcTrappedPaper)
                    {
                        cmd.Parameters.AddWithValue("@NC_TrappedPaper", record.NCTrappedPaper ?? (object)DBNull.Value);
                    }

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void InsertProduzione(
      string impianto,
      string lotto,
      string turno,
      string operatore,
      string prodotto,
      string ricetta,
      string recipeParameterTopJson,
      string recipeParameterSideJson,
      string recipeParameterFrontJson,
      string recipeParameterTop3DJson,
      string lastParameterJson,
      string ConfigParameterJson,
      DateTime timeCreationIdpro,
      DateTime timeInizio,
      int? type)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                // Query aggiornata con le nuove colonne
                string query = @"INSERT INTO tblproduzione (
            Impianto, Lotto, Turno, Operatore, Prodotto, Ricetta,
            RecipeParamerterTop, RecipeParamerterSide, RecipeParamerterFront, RecipeParamerterTop3D, LastParameter, ConfigParameter, TimeCreationIdpro,
            TimeInizio, TimeFine, type, IsDeleted, CreatedAt)
            VALUES (
            @Impianto, @Lotto, @Turno, @Operatore, @Prodotto, @Ricetta,
            @RecipeParamerterTop, @RecipeParamerterSide, @RecipeParamerterFront, @RecipeParamerterTop3D, @LastParameter, @ConfigParameter, @TimeCreationIdpro,
            @TimeInizio, @TimeFine, @Type, FALSE, @CreatedAt);";

                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Impianto", impianto ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Lotto", lotto ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Turno", turno ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Operatore", operatore ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Prodotto", prodotto ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Ricetta", ricetta ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@RecipeParamerterTop", recipeParameterTopJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@RecipeParamerterSide", recipeParameterSideJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@RecipeParamerterFront", recipeParameterFrontJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@RecipeParamerterTop3D", recipeParameterTop3DJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LastParameter", lastParameterJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ConfigParameter", ConfigParameterJson ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@TimeCreationIdpro", timeCreationIdpro);
                    cmd.Parameters.AddWithValue("@TimeInizio", timeInizio);
                    cmd.Parameters.AddWithValue("@TimeFine", DBNull.Value);
                    cmd.Parameters.AddWithValue("@Type", type ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void InsertLogEvent(
            string application,
            DateTime timestamp,
            string level,
            string logId,
            string infoJson,
            int? exportType)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();
                    string query = @"INSERT INTO tbllogevent (
                application, timestamp, level, log_id, info, export_type)
                VALUES (
                @Application, @Timestamp, @Level, @LogId, @Info, @ExportType);";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Application", application ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Timestamp", timestamp);
                        cmd.Parameters.AddWithValue("@Level", level ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@LogId", logId ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Info", infoJson ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@ExportType", exportType ?? (object)DBNull.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to insert log event: " + ex.Message + "\n" + ex.StackTrace);
                throw; // Rethrow the exception for further handling if needed
            }

        }
        public void UpdateProduzione(
            long idProduzione,
            DateTime? timeFine = null,
            string recipeParameterTopJson = null,
            string recipeParameterSideJson = null,
            string recipeParameterFrontJson = null,
            string recipeParameterTop3DJson = null,
            string lastParameterJson = null,
             string ConfigParameterJson = null)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                var queryBuilder = new StringBuilder("UPDATE tblproduzione SET ");
                var parameters = new List<MySqlParameter>();

                if (timeFine.HasValue)
                {
                    queryBuilder.Append("TimeFine = @TimeFine, ");
                    parameters.Add(new MySqlParameter("@TimeFine", timeFine.Value));
                }

                if (recipeParameterTopJson != null)
                {
                    queryBuilder.Append("RecipeParamerterTop = @RecipeParamerterTop, ");
                    parameters.Add(new MySqlParameter("@RecipeParamerterTop", recipeParameterTopJson));
                }

                if (recipeParameterSideJson != null)
                {
                    queryBuilder.Append("RecipeParamerterSide = @RecipeParamerterSide, ");
                    parameters.Add(new MySqlParameter("@RecipeParamerterSide", recipeParameterSideJson));
                }

                if (recipeParameterFrontJson != null)
                {
                    queryBuilder.Append("RecipeParamerterFront = @RecipeParamerterFront, ");
                    parameters.Add(new MySqlParameter("@RecipeParamerterFront", recipeParameterFrontJson));
                }
                if (recipeParameterTop3DJson != null)
                {
                    queryBuilder.Append("RecipeParamerterTop3D = @RecipeParamerterTop3D, ");
                    parameters.Add(new MySqlParameter("@RecipeParamerterTop3D", recipeParameterTop3DJson));
                }

                if (lastParameterJson != null)
                {
                    queryBuilder.Append("LastParameter = @LastParameter, ");
                    parameters.Add(new MySqlParameter("@LastParameter", lastParameterJson));
                }
                if (ConfigParameterJson != null)
                {
                    queryBuilder.Append("ConfigParameter = @ConfigParameter, ");
                    parameters.Add(new MySqlParameter("@ConfigParameter", ConfigParameterJson));
                }

                // Rimuove l'ultima virgola
                queryBuilder.Length -= 2;
                queryBuilder.Append(" WHERE IdProduzione = @IdProduzione;");
                parameters.Add(new MySqlParameter("@IdProduzione", idProduzione));

                using (var cmd = new MySqlCommand(queryBuilder.ToString(), conn))
                {
                    cmd.Parameters.AddRange(parameters.ToArray());
                    cmd.ExecuteNonQuery();
                }
            }
        }

      
        public async Task<bool> ExistsRicettaAsync(string ricetta)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    // Considera solo le ricette non cancellate
                    string query = "SELECT IdProduzione FROM tblproduzione WHERE Ricetta = @Ricetta AND IsDeleted = FALSE LIMIT 1";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Ricetta", NormalizeRecipeDatabaseName(ricetta));

                        var result = await cmd.ExecuteScalarAsync();

                        if (result != null && long.TryParse(result.ToString(), out long idProduzione))
                        {
                            MainWindow._produzioneRecord.IdProduzione = idProduzione;
                            return true;
                        }

                        return false;
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to check if ricetta exists: " + ex.Message + "\n" + ex.StackTrace);
                throw;
            }
        }

        public async Task UpdateStartTimeAsync(string ricetta, DateTime newStartTime)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string query = "UPDATE tblproduzione SET TimeInizio = @TimeInizio WHERE Ricetta = @Ricetta";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@TimeInizio", newStartTime);
                        cmd.Parameters.AddWithValue("@Ricetta", ricetta);

                        await cmd.ExecuteNonQueryAsync();
                    }
                    conn.Close();
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to update start time: " + ex.Message + "\n" + ex.StackTrace);
                throw; // Rethrow the exception for further handling if needed
            }


        }
        public async Task UpdateEndTimeAsync(string ricetta, DateTime newEndTime)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    string query = "UPDATE tblproduzione SET TimeFine = @TimeFine WHERE Ricetta = @Ricetta";
                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@TimeFine", newEndTime);
                        cmd.Parameters.AddWithValue("@Ricetta", ricetta);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    conn.Close();
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to update end time: " + ex.Message + "\n" + ex.StackTrace);
                throw; // Rethrow the exception for further handling if needed
            }
        }
        public async Task udpdatePameterRecipe(string ricetta,
             string recipeParameterTopJson = null,
            string recipeParameterSideJson = null,
            string recipeParameterFrontJson = null,
            string recipeParameterTop3DJson = null,
            string lastParameterJson = null,
             string ConfigParameterJson = null)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";
            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var queryBuilder = new StringBuilder("UPDATE tblproduzione SET ");
                    var parameters = new List<MySqlParameter>();
                    if (recipeParameterTopJson != null)
                    {
                        queryBuilder.Append("RecipeParamerterTop = @RecipeParamerterTop, ");
                        parameters.Add(new MySqlParameter("@RecipeParamerterTop", recipeParameterTopJson));
                    }

                    if (recipeParameterSideJson != null)
                    {
                        queryBuilder.Append("RecipeParamerterSide = @RecipeParamerterSide, ");
                        parameters.Add(new MySqlParameter("@RecipeParamerterSide", recipeParameterSideJson));
                    }
                    if (recipeParameterFrontJson != null)
                    {
                        queryBuilder.Append("RecipeParamerterFront = @RecipeParamerterFront, ");
                        parameters.Add(new MySqlParameter("@RecipeParamerterFront", recipeParameterFrontJson));
                    }
                    if (recipeParameterTop3DJson != null)
                    {
                        queryBuilder.Append("RecipeParamerterTop3D = @RecipeParamerterTop3D, ");
                        parameters.Add(new MySqlParameter("@RecipeParamerterTop3D", recipeParameterTop3DJson));
                    }
                    if (lastParameterJson != null)
                    {
                        queryBuilder.Append("LastParameter = @LastParameter, ");
                        parameters.Add(new MySqlParameter("@LastParameter", lastParameterJson));
                    }
                    if (ConfigParameterJson != null)
                    {
                        queryBuilder.Append("ConfigParameter = @ConfigParameter, ");
                        parameters.Add(new MySqlParameter("@ConfigParameter", ConfigParameterJson));
                    }
                    // Rimuove l'ultima virgola
                    queryBuilder.Length -= 2;
                    queryBuilder.Append(" WHERE Ricetta = @Ricetta;");
                    parameters.Add(new MySqlParameter("@Ricetta", ricetta));
                    using (var cmd = new MySqlCommand(queryBuilder.ToString(), conn))
                    {
                        cmd.Parameters.AddRange(parameters.ToArray());
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to update recipe parameters: " + ex.Message + "\n" + ex.StackTrace);
                throw; // Rethrow the exception for further handling if needed
            }
        }


        // Metodo per Soft Delete (cancellazione logica)
        public void SoftDeleteRicetta(int idProduzione)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                string query = "UPDATE tblproduzione SET IsDeleted = TRUE, UpdatedAt = @UpdatedAt WHERE IdProduzione = @IdProduzione";

                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@IdProduzione", idProduzione);
                    cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        // Overload per cancellare per nome ricetta
        public void SoftDeleteRicetta(string nomeRicetta)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                string query = "UPDATE tblproduzione SET IsDeleted = TRUE, UpdatedAt = @UpdatedAt WHERE Ricetta = @Ricetta";

                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Ricetta", nomeRicetta);
                    cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        // Metodo per rinominare la ricetta
        public void RenameRicetta(int idProduzione, string nuovoNomeRicetta)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                string query = "UPDATE tblproduzione SET Ricetta = @NuovoNome, UpdatedAt = @UpdatedAt WHERE IdProduzione = @IdProduzione AND IsDeleted = FALSE";

                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@IdProduzione", idProduzione);
                    cmd.Parameters.AddWithValue("@NuovoNome", nuovoNomeRicetta);
                    cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        // Metodo per ripristinare una ricetta cancellata
        public void RipristinaRicetta(int idProduzione)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            using (var conn = new MySqlConnection(connectionString))
            {
                conn.Open();

                string query = "UPDATE tblproduzione SET IsDeleted = FALSE, UpdatedAt = @UpdatedAt WHERE IdProduzione = @IdProduzione";

                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@IdProduzione", idProduzione);
                    cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        public int? GetRicettaIdByName(string nomeRicetta)
        {
            var config = MainWindow.configManager.Config.MySqlConnection;
            string connectionString = $"Server={config.Host};Database={config.Db};Uid={config.User};Pwd={config.Password};Port={config.port}";

            try
            {
                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    string query = "SELECT IdProduzione FROM tblproduzione WHERE Ricetta = @Ricetta AND IsDeleted = FALSE LIMIT 1";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Ricetta", nomeRicetta);

                        var result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : (int?)null;
                    }
                }
            }
            catch (MySqlException ex)
            {
                MainWindow.logger?.Error("Failed to get ricetta ID: " + ex.Message + "\n" + ex.StackTrace);
                return null;
            }
        }
    }
}
