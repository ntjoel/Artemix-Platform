using System.Diagnostics;
using System.Drawing;

namespace QtisVision.Setup;

internal sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(15, 47, 76);
    private static readonly Color Accent = Color.FromArgb(229, 112, 20);
    private static readonly Color PanelBackground = Color.FromArgb(245, 248, 251);
    private readonly InstallerEngine _engine;
    private readonly Label _statusLabel = new();
    private readonly ListView _componentsList = new();
    private readonly RichTextBox _logBox = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _progressLabel = new();
    private readonly Button _validateButton = new();
    private readonly Button _installButton = new();
    private readonly Button _openLogButton = new();
    private readonly Button _closeButton = new();
    private bool _busy;

    public MainForm(string manifestPath)
    {
        _engine = new InstallerEngine(manifestPath);
        _engine.LogMessage += OnLogMessage;
        _engine.ProgressChanged += OnProgressChanged;

        Text = _engine.Manifest.IsUpdateOnly
            ? $"Qtis Vision Update {_engine.Manifest.ProductVersion}-r{_engine.Manifest.MediaRevision}"
            : $"Qtis Vision Setup {_engine.Manifest.ProductVersion}-r{_engine.Manifest.MediaRevision}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 640);
        Size = new Size(1060, 760);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PanelBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
        PopulateComponents();
        RefreshDetection();
        AppendManifestWarnings();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _engine.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            MessageBox.Show(
                "Attendere il completamento dell'operazione corrente.",
                "Qtis Vision Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        base.OnFormClosing(e);
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        Controls.Add(root);

        Panel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Navy,
            Padding = new Padding(22, 12, 22, 10)
        };
        Label title = new()
        {
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            Text = _engine.Manifest.IsUpdateOnly
                ? "Qtis Vision Panel Update"
                : "Qtis Vision Panel"
        };
        Label subtitle = new()
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 225, 238),
            Font = new Font("Segoe UI", 9.5F),
            Location = new Point(24, 52),
            Text = _engine.Manifest.IsUpdateOnly
                ? $"Aggiornamento applicativo protetto - release {_engine.Manifest.ProductVersion}-r{_engine.Manifest.MediaRevision}"
                : $"Installazione industriale offline - release {_engine.Manifest.ProductVersion}-r{_engine.Manifest.MediaRevision}"
        };
        title.Location = new Point(22, 10);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        root.Controls.Add(header, 0, 0);

        Panel statusPanel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(14, 10, 14, 8)
        };
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _statusLabel.ForeColor = Navy;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusPanel.Controls.Add(_statusLabel);
        root.Controls.Add(statusPanel, 0, 1);

        GroupBox componentsGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = "Componenti",
            Padding = new Padding(10)
        };
        _componentsList.Dock = DockStyle.Fill;
        _componentsList.CheckBoxes = true;
        _componentsList.FullRowSelect = true;
        _componentsList.GridLines = true;
        _componentsList.HideSelection = false;
        _componentsList.View = View.Details;
        _componentsList.Columns.Add("Componente", 250);
        _componentsList.Columns.Add("Stato", 135);
        _componentsList.Columns.Add("Modalita", 120);
        _componentsList.Columns.Add("Dettaglio", 460);
        _componentsList.ItemCheck += OnComponentItemCheck;
        componentsGroup.Controls.Add(_componentsList);
        root.Controls.Add(componentsGroup, 0, 2);

        GroupBox logGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = "Attivita e diagnostica",
            Padding = new Padding(10)
        };
        _logBox.Dock = DockStyle.Fill;
        _logBox.BackColor = Color.FromArgb(20, 28, 36);
        _logBox.ForeColor = Color.FromArgb(221, 232, 239);
        _logBox.BorderStyle = BorderStyle.FixedSingle;
        _logBox.Font = new Font("Consolas", 8.5F);
        _logBox.ReadOnly = true;
        logGroup.Controls.Add(_logBox);
        root.Controls.Add(logGroup, 0, 3);

        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            Padding = new Padding(0, 10, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _progressLabel.Dock = DockStyle.Fill;
        _progressLabel.ForeColor = Navy;
        _progressLabel.Text = "Pronto";
        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Style = ProgressBarStyle.Continuous;

        ConfigureButton(_validateButton, "Verifica", Color.White, Navy);
        ConfigureButton(
            _installButton,
            _engine.Manifest.IsUpdateOnly ? "Aggiorna applicazione" : "Installa / aggiorna",
            Accent,
            Color.White);
        ConfigureButton(_openLogButton, "Apri log", Color.White, Navy);
        ConfigureButton(_closeButton, "Chiudi", Color.White, Navy);
        _validateButton.Click += async (_, _) => await ValidateSelectedAsync();
        _installButton.Click += async (_, _) => await InstallSelectedAsync();
        _openLogButton.Click += (_, _) => OpenLog();
        _closeButton.Click += (_, _) => Close();

        footer.Controls.Add(_progressLabel, 0, 0);
        footer.SetColumnSpan(_progressLabel, 5);
        footer.Controls.Add(_progressBar, 0, 1);
        footer.Controls.Add(_validateButton, 1, 1);
        footer.Controls.Add(_installButton, 2, 1);
        footer.Controls.Add(_openLogButton, 3, 1);
        footer.Controls.Add(_closeButton, 4, 1);
        root.Controls.Add(footer, 0, 4);
    }

    private void PopulateComponents()
    {
        _componentsList.BeginUpdate();
        try
        {
            _componentsList.Items.Clear();
            foreach (InstallerComponent component in _engine.Manifest.Components)
            {
                ListViewItem item = new(component.DisplayName)
                {
                    Tag = component,
                    Checked = component.DefaultSelected && component.Enabled
                };
                item.SubItems.Add("Da verificare");
                item.SubItems.Add(component.Interactive ? "Guidata" : "Automatica");
                item.SubItems.Add(component.Enabled
                    ? component.Description
                    : component.BlockingReason ?? component.Description);
                if (!component.Enabled)
                {
                    item.ForeColor = Color.DarkRed;
                }
                _componentsList.Items.Add(item);
            }
        }
        finally
        {
            _componentsList.EndUpdate();
        }
    }

    private void RefreshDetection()
    {
        int installed = 0;
        int missing = 0;
        foreach (ListViewItem item in _componentsList.Items)
        {
            if (item.Tag is not InstallerComponent component)
            {
                continue;
            }

            ComponentDetection detection = _engine.Detect(component);
            if (component.Kind.Equals("qtis-core", StringComparison.OrdinalIgnoreCase))
            {
                CoreInstallationInfo coreInfo = _engine.GetCoreInstallationInfo();
                item.SubItems[1].Text = coreInfo.DisplayText;
                item.BackColor = coreInfo.Mode switch
                {
                    "Fresh" => Color.White,
                    "Upgrade" => Color.FromArgb(255, 244, 218),
                    "Repair" => Color.FromArgb(228, 244, 234),
                    _ => Color.FromArgb(255, 229, 229)
                };
                if (coreInfo.Mode == "Repair")
                {
                    installed++;
                }
                else
                {
                    missing++;
                }
                continue;
            }

            if (component.Kind.Equals("cognex-gige-network", StringComparison.OrdinalIgnoreCase) ||
                component.Kind.Equals("qtis-autostart", StringComparison.OrdinalIgnoreCase))
            {
                item.SubItems[1].Text = component.Kind.Equals(
                    "qtis-autostart",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Avvio ritardato automatico"
                    : "Configurazione automatica";
                item.BackColor = Color.FromArgb(228, 240, 249);
                missing++;
                continue;
            }

            item.SubItems[1].Text = DetectionText(detection);
            switch (detection)
            {
                case ComponentDetection.Installed:
                    item.BackColor = Color.FromArgb(228, 244, 234);
                    if (!component.Required &&
                        !component.Kind.Equals("python-offline", StringComparison.OrdinalIgnoreCase) &&
                        !component.ConfigureWhenInstalled)
                    {
                        item.Checked = false;
                    }
                    installed++;
                    break;
                case ComponentDetection.InstalledOlder:
                    item.BackColor = Color.FromArgb(255, 244, 218);
                    missing++;
                    break;
                case ComponentDetection.Missing:
                    item.BackColor = Color.White;
                    missing++;
                    break;
                case ComponentDetection.Invalid:
                    item.BackColor = Color.FromArgb(255, 229, 229);
                    missing++;
                    break;
                default:
                    item.BackColor = Color.White;
                    missing++;
                    break;
            }
        }

        _statusLabel.Text =
            $"Destinazione: {_engine.Manifest.InstallRoot}     Installati: {installed}     Da installare/aggiornare: {missing}";
    }

    private void AppendManifestWarnings()
    {
        foreach (string warning in _engine.Manifest.Warnings)
        {
            AppendLog($"AVVISO | {warning}");
        }
        AppendLog($"Log installer: {_engine.LogPath}");
    }

    private async Task ValidateSelectedAsync()
    {
        IReadOnlyCollection<string> selected = SelectedComponentIds();
        if (selected.Count == 0)
        {
            MessageBox.Show("Selezionare almeno un componente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "Verifica integrita payload...");
        try
        {
            ValidationReport report = await _engine.ValidateAsync(selected, CancellationToken.None);
            foreach (ValidationFinding finding in report.Findings.Where(finding => finding.Severity != "Info"))
            {
                AppendLog($"{finding.Severity.ToUpperInvariant()} | {finding.Message}");
            }

            MessageBox.Show(
                report.IsValid
                    ? "Verifica completata. I payload selezionati sono integri."
                    : string.Join(
                        Environment.NewLine,
                        report.Findings.Where(item => item.BlocksInstallation).Select(item => item.Message)),
                "Verifica installer",
                MessageBoxButtons.OK,
                report.IsValid ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            ShowFailure("Verifica fallita", ex);
        }
        finally
        {
            SetBusy(false, "Pronto");
        }
    }

    private async Task InstallSelectedAsync()
    {
        IReadOnlyCollection<string> selected = SelectedComponentIds();
        if (selected.Count == 0)
        {
            MessageBox.Show("Selezionare almeno un componente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string coreAction = selected.Contains("qtis-core", StringComparer.OrdinalIgnoreCase)
            ? _engine.GetCoreInstallationInfo().DisplayText
            : "HMI non selezionata";
        bool configuresGigE = selected.Contains("cognex-gige-network", StringComparer.OrdinalIgnoreCase);
        bool configuresAutoStart = selected.Contains("qtis-autostart", StringComparer.OrdinalIgnoreCase);
        DialogResult confirmation = MessageBox.Show(
            "L'installer verifichera i payload, eseguira i prerequisiti selezionati e installera la HMI.\n\n" +
            $"Operazione HMI: {coreAction}.\n" +
            "L'aggiornamento e differenziale: vengono sostituiti solo i file applicativi cambiati, con backup e rollback.\n" +
            "Configurazioni macchina, ricette, job VPP, certificati OPC e modelli AI esistenti non saranno sovrascritti.\n" +
            (configuresGigE
                ? "Le sole schede camera Cognex GigE riconosciute senza ambiguita saranno ottimizzate; lo stato precedente verra salvato.\n"
                : string.Empty) +
            (configuresAutoStart
                ? "Verranno configurati l'avvio HMI ritardato, l'attesa servizi e la protezione contro il doppio avvio.\n"
                : string.Empty) +
            "\nContinuare?",
            "Conferma installazione",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        SetBusy(
            true,
            _engine.Manifest.IsUpdateOnly
                ? "Aggiornamento applicazione in corso..."
                : "Installazione in corso...");
        try
        {
            await _engine.InstallAsync(selected, CancellationToken.None);
            RefreshDetection();
            string completionMessage = _engine.Manifest.IsUpdateOnly
                ? "Aggiornamento completato.\n\nSono stati elaborati soltanto i file applicativi gestiti. Ricette, job VPP, Programs e configurazioni macchina sono rimasti invariati."
                : "Installazione completata.\n\nVerificare licenze VisionPro, configurazione MySQL, report rete Cognex GigE, avvio automatico e collegamenti hardware prima di avviare la produzione.";
            MessageBox.Show(
                completionMessage,
                _engine.Manifest.IsUpdateOnly ? "Qtis Vision Update" : "Qtis Vision Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowFailure("Installazione non completata", ex);
        }
        finally
        {
            SetBusy(false, "Pronto");
        }
    }

    private IReadOnlyCollection<string> SelectedComponentIds()
    {
        return _componentsList.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag as InstallerComponent)
            .Where(component => component is not null)
            .Select(component => component!.Id)
            .ToArray();
    }

    private void OnComponentItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_busy)
        {
            e.NewValue = e.CurrentValue;
            return;
        }

        if (_componentsList.Items[e.Index].Tag is not InstallerComponent component)
        {
            return;
        }

        if (!component.Enabled)
        {
            e.NewValue = CheckState.Unchecked;
            return;
        }

        if (component.Required && e.NewValue == CheckState.Unchecked)
        {
            e.NewValue = CheckState.Checked;
        }
    }

    private void OnLogMessage(string line)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
        }
        else
        {
            AppendLog(line);
        }
    }

    private void OnProgressChanged(int percent, string message)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateProgress(percent, message));
        }
        else
        {
            UpdateProgress(percent, message);
        }
    }

    private void UpdateProgress(int percent, string message)
    {
        _progressBar.Value = Math.Clamp(percent, 0, 100);
        _progressLabel.Text = message;
    }

    private void AppendLog(string line)
    {
        _logBox.AppendText(line + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void SetBusy(bool busy, string message)
    {
        _busy = busy;
        _validateButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
        _componentsList.Enabled = !busy;
        UseWaitCursor = busy;
        _progressLabel.Text = message;
        if (!busy)
        {
            _progressBar.Value = 0;
        }
    }

    private void OpenLog()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_engine.LogPath}\"",
            UseShellExecute = true
        });
    }

    private void ShowFailure(string title, Exception ex)
    {
        AppendLog($"ERRORE | {ex}");
        MessageBox.Show(
            $"{ex.Message}\n\nDettagli: {_engine.LogPath}",
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void ConfigureButton(Button button, string text, Color background, Color foreground)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(8, 2, 0, 2);
        button.Text = text;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Navy;
        button.FlatAppearance.BorderSize = 1;
        button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
    }

    private static string DetectionText(ComponentDetection detection) => detection switch
    {
        ComponentDetection.Installed => "Gia installato",
        ComponentDetection.InstalledOlder => "Da aggiornare",
        ComponentDetection.Missing => "Mancante",
        ComponentDetection.Invalid => "Non disponibile",
        _ => "Da verificare"
    };
}
