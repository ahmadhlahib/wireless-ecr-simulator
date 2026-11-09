// Wireless ECR Simulator - by Ahmad El Lahib
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WirelessEcrSim
{
    public class LogEntry
    {
        public string Time { get; set; }
        public string Tag { get; set; }
        public string Text { get; set; }
        public Brush TagBack { get; set; }
        public Brush TagFore { get; set; }
    }

    public class HistoryRow
    {
        public string Time { get; set; }
        public string Type { get; set; }
        public string Ref { get; set; }
        public string Amount { get; set; }
        public string Code { get; set; }
        public string Desc { get; set; }
        public string Stan { get; set; }
        public string Ms { get; set; }
    }

    /// <summary>Loads MainWindow.xaml and drives it: merchant calls, virtual terminal, door flow, logs.</summary>
    public class MainController
    {
        public const string Version = "1.0";
        private const int MaxLogLines = 4000;

        public Window Window { get; private set; }

        private readonly AppSettings settings;
        private EnvProfile profile;
        private readonly VirtualTerminal terminal = new VirtualTerminal();
        private readonly MockMerchant mock = new MockMerchant();
        private readonly ObservableCollection<LogEntry> logs = new ObservableCollection<LogEntry>();
        private readonly ObservableCollection<HistoryRow> history = new ObservableCollection<HistoryRow>();
        private readonly ObservableCollection<IncomingRequest> pending = new ObservableCollection<IncomingRequest>();
        private readonly string logFile;
        private readonly object logFileLock = new object();
        private readonly DispatcherTimer waitTimer = new DispatcherTimer();
        private DateTime waitStarted;
        private int waitTotalSec;
        private bool busy;
        private string lastRef;
        private string lastStan;
        private string lastBatch;
        private DateTime lastBoonzTrigger = DateTime.MinValue;
        private bool loadingUi;

        // ---- controls ----
        private ComboBox EnvCombo, CurrencyCombo, ModeCombo, DeclineCodeCombo, MockReplyCombo;
        private TextBox ServerBox, WsBox, TerminalBox, TokenBox, AmountBox, RefBox, StanBox, TipsBox, WaitBox,
            RetryCodeBox, RetryStepBox, RetryMinBox, ResponseBox, RequestBox, CountryBox, DeclineDescBox, DelayBox,
            LockBox, DoorRefBox, PreauthAmountBox, PreauthDelayBox, MockPortBox, MockConfigBox;
        private PasswordBox PassBox, SecretBox, MockSecretBox;
        private TextBlock TokenInfo, TokenChip, TermChip, MockChip, TypeHint, AmountHint, SendBtnText, WaitText,
            ResultBadgeText, ResultTitle, ResultDesc, ResultExplain, FactCode, FactRef, FactStan, FactBatch, FactHttp, FactTime,
            TermStateText, TermStateSub, PendingEmpty;
        private Ellipse TokenDot, TermDot, MockDot, TermStateDot;
        private Border ProdBadge, ProdBanner, ResultBadge;
        private RadioButton TypeSale, TypePreauth, TypeIncremental, TypeCompletion, TypeVoid, TypeSettle;
        private CheckBox AutoRefCheck, RetryCheck, ModernCheck, AutoReconnectCheck, DuplicateCheck, BoonzCheck, PretendCheck, ProdCheck;
        private Button GetTokenBtn, SaveBtn, SendBtn, TrxInfoBtn, PosTrxInfoBtn, CurlBtn, ConnectBtn, DropBtn,
            OpenDoorApiBtn, OpenDoorWsBtn, MockToggleBtn, CopyLogBtn, OpenLogsBtn, ClearLogBtn, AboutBtn, NewEnvBtn, RenameEnvBtn, DeleteEnvBtn;
        private FrameworkElement AmountPanel, CurrencyPanel, StanPanel, TipsPanel, WaitPanel, DeclineRow;
        private ItemsControl PendingList;
        private ListBox LogList;
        private ListView HistoryList;

        public MainController()
        {
            settings = AppSettings.Load();
            string logDir = System.IO.Path.Combine(Program.AppDir, "logs");
            Directory.CreateDirectory(logDir);
            logFile = System.IO.Path.Combine(logDir, "wireless-ecr-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

            Window = LoadWindow();
            Bind();
            Wire();
            LoadUi();
            AddLog("INFO", "Wireless ECR Simulator " + Version + " started. Settings: " + AppSettings.FilePath);
        }

        // =========================================================== setup

        private static Window LoadWindow()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream stream = asm.GetManifestResourceStream("WirelessEcrSim.MainWindow.xaml");
            if (stream == null)
            {
                string file = System.IO.Path.Combine(Program.AppDir, "src", "MainWindow.xaml");
                stream = File.OpenRead(file);
            }
            using (stream)
            {
                Window w = (Window)XamlReader.Load(stream);
                Stream icon = asm.GetManifestResourceStream("WirelessEcrSim.app.ico");
                string iconFile = System.IO.Path.Combine(Program.AppDir, "assets", "app.ico");
                if (icon == null && File.Exists(iconFile)) icon = new MemoryStream(File.ReadAllBytes(iconFile));
                if (icon != null)
                {
                    try { w.Icon = BitmapFrame.Create(icon); } catch { }
                }
                return w;
            }
        }

        private T F<T>(string name) where T : class
        {
            T c = Window.FindName(name) as T;
            if (c == null) throw new InvalidOperationException("MainWindow.xaml has no " + typeof(T).Name + " named " + name);
            return c;
        }

        private void Bind()
        {
            EnvCombo = F<ComboBox>("EnvCombo"); CurrencyCombo = F<ComboBox>("CurrencyCombo"); ModeCombo = F<ComboBox>("ModeCombo");
            DeclineCodeCombo = F<ComboBox>("DeclineCodeCombo"); MockReplyCombo = F<ComboBox>("MockReplyCombo");
            ServerBox = F<TextBox>("ServerBox"); WsBox = F<TextBox>("WsBox"); TerminalBox = F<TextBox>("TerminalBox");
            TokenBox = F<TextBox>("TokenBox"); AmountBox = F<TextBox>("AmountBox"); RefBox = F<TextBox>("RefBox");
            StanBox = F<TextBox>("StanBox"); TipsBox = F<TextBox>("TipsBox"); WaitBox = F<TextBox>("WaitBox");
            RetryCodeBox = F<TextBox>("RetryCodeBox"); RetryStepBox = F<TextBox>("RetryStepBox"); RetryMinBox = F<TextBox>("RetryMinBox");
            ResponseBox = F<TextBox>("ResponseBox"); RequestBox = F<TextBox>("RequestBox"); CountryBox = F<TextBox>("CountryBox");
            DeclineDescBox = F<TextBox>("DeclineDescBox"); DelayBox = F<TextBox>("DelayBox"); LockBox = F<TextBox>("LockBox");
            DoorRefBox = F<TextBox>("DoorRefBox"); PreauthAmountBox = F<TextBox>("PreauthAmountBox");
            PreauthDelayBox = F<TextBox>("PreauthDelayBox"); MockPortBox = F<TextBox>("MockPortBox"); MockConfigBox = F<TextBox>("MockConfigBox");
            PassBox = F<PasswordBox>("PassBox"); SecretBox = F<PasswordBox>("SecretBox"); MockSecretBox = F<PasswordBox>("MockSecretBox");
            TokenInfo = F<TextBlock>("TokenInfo"); TokenChip = F<TextBlock>("TokenChip"); TermChip = F<TextBlock>("TermChip");
            MockChip = F<TextBlock>("MockChip"); TypeHint = F<TextBlock>("TypeHint"); AmountHint = F<TextBlock>("AmountHint");
            SendBtnText = F<TextBlock>("SendBtnText"); WaitText = F<TextBlock>("WaitText"); ResultBadgeText = F<TextBlock>("ResultBadgeText");
            ResultTitle = F<TextBlock>("ResultTitle"); ResultDesc = F<TextBlock>("ResultDesc"); ResultExplain = F<TextBlock>("ResultExplain");
            FactCode = F<TextBlock>("FactCode"); FactRef = F<TextBlock>("FactRef"); FactStan = F<TextBlock>("FactStan");
            FactBatch = F<TextBlock>("FactBatch"); FactHttp = F<TextBlock>("FactHttp"); FactTime = F<TextBlock>("FactTime");
            TermStateText = F<TextBlock>("TermStateText"); TermStateSub = F<TextBlock>("TermStateSub"); PendingEmpty = F<TextBlock>("PendingEmpty");
            TokenDot = F<Ellipse>("TokenDot"); TermDot = F<Ellipse>("TermDot"); MockDot = F<Ellipse>("MockDot"); TermStateDot = F<Ellipse>("TermStateDot");
            ProdBadge = F<Border>("ProdBadge"); ProdBanner = F<Border>("ProdBanner"); ResultBadge = F<Border>("ResultBadge");
            TypeSale = F<RadioButton>("TypeSale"); TypePreauth = F<RadioButton>("TypePreauth"); TypeIncremental = F<RadioButton>("TypeIncremental");
            TypeCompletion = F<RadioButton>("TypeCompletion"); TypeVoid = F<RadioButton>("TypeVoid"); TypeSettle = F<RadioButton>("TypeSettle");
            AutoRefCheck = F<CheckBox>("AutoRefCheck"); RetryCheck = F<CheckBox>("RetryCheck"); ModernCheck = F<CheckBox>("ModernCheck");
            AutoReconnectCheck = F<CheckBox>("AutoReconnectCheck"); DuplicateCheck = F<CheckBox>("DuplicateCheck");
            BoonzCheck = F<CheckBox>("BoonzCheck"); PretendCheck = F<CheckBox>("PretendCheck"); ProdCheck = F<CheckBox>("ProdCheck");
            GetTokenBtn = F<Button>("GetTokenBtn"); SaveBtn = F<Button>("SaveBtn"); SendBtn = F<Button>("SendBtn");
            TrxInfoBtn = F<Button>("TrxInfoBtn"); PosTrxInfoBtn = F<Button>("PosTrxInfoBtn"); CurlBtn = F<Button>("CurlBtn");
            ConnectBtn = F<Button>("ConnectBtn"); DropBtn = F<Button>("DropBtn"); OpenDoorApiBtn = F<Button>("OpenDoorApiBtn");
            OpenDoorWsBtn = F<Button>("OpenDoorWsBtn"); MockToggleBtn = F<Button>("MockToggleBtn"); CopyLogBtn = F<Button>("CopyLogBtn");
            OpenLogsBtn = F<Button>("OpenLogsBtn"); ClearLogBtn = F<Button>("ClearLogBtn"); AboutBtn = F<Button>("AboutBtn");
            NewEnvBtn = F<Button>("NewEnvBtn"); RenameEnvBtn = F<Button>("RenameEnvBtn"); DeleteEnvBtn = F<Button>("DeleteEnvBtn");
            AmountPanel = F<FrameworkElement>("AmountPanel"); CurrencyPanel = F<FrameworkElement>("CurrencyPanel");
            StanPanel = F<FrameworkElement>("StanPanel"); TipsPanel = F<FrameworkElement>("TipsPanel");
            WaitPanel = F<FrameworkElement>("WaitPanel"); DeclineRow = F<FrameworkElement>("DeclineRow");
            PendingList = F<ItemsControl>("PendingList"); LogList = F<ListBox>("LogList"); HistoryList = F<ListView>("HistoryList");
        }

        private void Wire()
        {
            LogList.ItemsSource = logs;
            HistoryList.ItemsSource = history;
            PendingList.ItemsSource = pending;
            pending.CollectionChanged += delegate { PendingEmpty.Visibility = pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };

            foreach (string c in new[] { "LBP", "USD", "IQD", "JOD", "EUR" }) CurrencyCombo.Items.Add(c);
            foreach (string m in new[] { "Approve", "Decline", "Ask me", "Don't answer (server times out)", "Busy (POS busy)" }) ModeCombo.Items.Add(m);
            foreach (string d in new[] { "-1", "1116", "1100", "1101", "1117", "9112" }) DeclineCodeCombo.Items.Add(d);
            foreach (string r in new[] { "200 success", "-1 signature error", "7002 timestamp expired", "HTTP 500", "No answer (timeout)" }) MockReplyCombo.Items.Add(r);

            EnvCombo.SelectionChanged += delegate { OnEnvChanged(); };
            foreach (RadioButton rb in new[] { TypeSale, TypePreauth, TypeIncremental, TypeCompletion, TypeVoid, TypeSettle })
                rb.Checked += delegate { OnTypeChanged(); };
            ModeCombo.SelectionChanged += delegate { UpdateModeUi(); };
            TokenBox.TextChanged += delegate { UpdateTokenUi(); };
            MockReplyCombo.SelectionChanged += delegate { mock.ReplyMode = MockReplyCombo.SelectedItem as string; };
            MockPortBox.TextChanged += delegate { UpdateMockUi(); };

            GetTokenBtn.Click += async delegate { await GetToken(); };
            SaveBtn.Click += delegate { SaveSettings(true); };
            SendBtn.Click += async delegate { await SendTransaction(); };
            TrxInfoBtn.Click += async delegate { await CheckTrx(false); };
            PosTrxInfoBtn.Click += async delegate { await CheckTrx(true); };
            CurlBtn.Click += delegate { CopyCurl(); };
            ConnectBtn.Click += delegate { ToggleTerminal(); };
            DropBtn.Click += delegate { terminal.DropLink(); AddLog("WS", "link dropped on purpose (no close frame)"); };
            OpenDoorApiBtn.Click += async delegate { await OpenDoorApi(); };
            OpenDoorWsBtn.Click += async delegate { await PressStart(); };
            MockToggleBtn.Click += delegate { ToggleMock(); };
            CopyLogBtn.Click += delegate { CopyLog(); };
            OpenLogsBtn.Click += delegate { Process.Start("explorer.exe", "\"" + System.IO.Path.GetDirectoryName(logFile) + "\""); };
            ClearLogBtn.Click += delegate { logs.Clear(); history.Clear(); };
            AboutBtn.Click += delegate { ShowAbout(); };
            NewEnvBtn.Click += delegate { NewEnvironment(); };
            RenameEnvBtn.Click += delegate { RenameEnvironment(); };
            DeleteEnvBtn.Click += delegate { DeleteEnvironment(); };
            ProdCheck.Click += delegate
            {
                profile.IsProduction = ProdCheck.IsChecked == true;
                ShowProduction();
                SaveSettings(false);
            };
            PendingList.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnPendingClick));

            terminal.Log += (tag, text) => AddLog(tag, text);
            terminal.StateChanged += s => Ui(() => UpdateTerminalUi(s));
            terminal.RequestReceived += r => Ui(() => OnTerminalRequest(r));
            terminal.DoorResult += (id, code, desc) => Ui(() => OnDoorResult(code, desc));
            mock.Log += (tag, text) => AddLog(tag, text);
            mock.DoorOpened += (tid, lockNum) => Ui(() => OnMockDoorOpened(tid, lockNum));

            waitTimer.Interval = TimeSpan.FromMilliseconds(250);
            waitTimer.Tick += delegate
            {
                int s = (int)(DateTime.Now - waitStarted).TotalSeconds;
                WaitText.Text = "Waiting for the terminal...  " + s + " s of " + waitTotalSec + " s";
            };

            Window.Closing += delegate
            {
                SaveSettings(false);
                try { terminal.Disconnect(); } catch { }
                try { mock.Stop(); } catch { }
            };
        }

        private void LoadUi()
        {
            loadingUi = true;
            foreach (EnvProfile p in settings.Profiles) EnvCombo.Items.Add(p.Name);
            int index = settings.Profiles.FindIndex(p => p.Name == settings.SelectedProfile);
            profile = settings.Profiles[index < 0 ? 0 : index];
            ProfileToUi();
            EnvCombo.SelectedIndex = index < 0 ? 0 : index;

            AmountBox.Text = settings.Amount;
            RefBox.Text = settings.NextEcrRefNo.ToString(CultureInfo.InvariantCulture);
            AutoRefCheck.IsChecked = settings.AutoIncrementRef;
            WaitBox.Text = settings.WaitingTimeSec.ToString(CultureInfo.InvariantCulture);
            RetryCheck.IsChecked = settings.RetryEnabled;
            RetryCodeBox.Text = settings.RetryCode;
            RetryStepBox.Text = settings.RetryStep;
            RetryMinBox.Text = settings.RetryMinAmount;

            ModernCheck.IsChecked = settings.TerminalModern;
            AutoReconnectCheck.IsChecked = settings.TerminalAutoReconnect;
            CountryBox.Text = settings.TerminalCountry;
            ModeCombo.SelectedItem = FindItem(ModeCombo, settings.TerminalMode) ?? ModeCombo.Items[0];
            DeclineCodeCombo.Text = settings.DeclineCode;
            DeclineDescBox.Text = settings.DeclineDesc;
            DelayBox.Text = Num(settings.AnswerDelaySec);

            BoonzCheck.IsChecked = settings.BoonzAutoPreauth;
            PretendCheck.IsChecked = settings.PretendDoorOpened;
            PreauthAmountBox.Text = settings.BoonzPreauthAmount;
            PreauthDelayBox.Text = Num(settings.BoonzPreauthDelaySec);
            LockBox.Text = settings.LockNum.ToString(CultureInfo.InvariantCulture);
            MockPortBox.Text = settings.MockMerchantPort.ToString(CultureInfo.InvariantCulture);
            MockReplyCombo.SelectedItem = FindItem(MockReplyCombo, settings.MockMerchantReply) ?? MockReplyCombo.Items[0];
            MockSecretBox.Password = settings.MockMerchantSecret ?? "";
            loadingUi = false;

            OnTypeChanged();
            UpdateModeUi();
            UpdateTerminalUi(TerminalState.Disconnected);
            UpdateMockUi();
            UpdateTokenUi();
        }

        private static object FindItem(ComboBox combo, string text)
        {
            if (text == null) return null;
            foreach (object o in combo.Items)
                if (string.Equals(o as string, text, StringComparison.OrdinalIgnoreCase)) return o;
            foreach (object o in combo.Items)
                if (((string)o).StartsWith(text, StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }

        // =========================================================== environment

        private void ProfileToUi()
        {
            ServerBox.Text = profile.Server;
            WsBox.Text = profile.WsUrl;
            TerminalBox.Text = profile.TerminalId;
            PassBox.Password = profile.Pass ?? "";
            SecretBox.Password = profile.SecretId ?? "";
            TokenBox.Text = profile.Token ?? "";
            if (!string.IsNullOrEmpty(profile.Currency)) CurrencyCombo.Text = profile.Currency;
            ProdCheck.IsChecked = profile.IsProduction;
            ShowProduction();
            UpdateTokenUi();
        }

        private void UiToProfile()
        {
            if (profile == null) return;
            profile.Server = ServerBox.Text.Trim();
            profile.WsUrl = WsBox.Text.Trim();
            profile.TerminalId = TerminalBox.Text.Trim();
            profile.Pass = PassBox.Password;
            profile.SecretId = SecretBox.Password;
            profile.Token = TokenBox.Text.Trim();
            profile.Currency = CurrencyCombo.Text.Trim();
        }

        private void ShowProduction()
        {
            Visibility prod = profile.IsProduction ? Visibility.Visible : Visibility.Collapsed;
            ProdBadge.Visibility = prod;
            ProdBanner.Visibility = prod;
        }

        private void NewEnvironment()
        {
            UiToProfile();
            string name = AskName("New environment", "Name (a copy of " + profile.Name + "):", profile.Name + " copy");
            if (name == null) return;
            if (string.Equals(name, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                Toast("An environment called " + name + " already exists.");
                return;
            }
            EnvProfile copy = profile.Copy();
            copy.Name = name;
            copy.Token = "";
            settings.Profiles.Add(copy);
            EnvCombo.Items.Add(name);
            EnvCombo.SelectedIndex = EnvCombo.Items.Count - 1;
            SaveSettings(false);
            AddLog("OK", "environment " + name + " added; edit its URLs and credentials, then Save");
        }

        private void RenameEnvironment()
        {
            string name = AskName("Rename environment", "New name for " + profile.Name + ":", profile.Name);
            if (name == null || name == profile.Name) return;
            int index = settings.Profiles.IndexOf(profile);
            AddLog("INFO", "environment " + profile.Name + " renamed to " + name);
            profile.Name = name;
            settings.SelectedProfile = name;
            loadingUi = true;
            EnvCombo.Items[index] = name;
            EnvCombo.SelectedIndex = index;
            loadingUi = false;
            SaveSettings(false);
        }

        private void DeleteEnvironment()
        {
            if (settings.Profiles.Count <= 1)
            {
                Toast("Keep at least one environment.");
                return;
            }
            if (MessageBox.Show(Window, "Delete the environment \"" + profile.Name + "\" and its saved password, secret and token?",
                "Delete environment", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (terminal.State != TerminalState.Disconnected) terminal.Disconnect();
            int index = settings.Profiles.IndexOf(profile);
            string removed = profile.Name;
            settings.Profiles.RemoveAt(index);
            loadingUi = true;
            EnvCombo.Items.RemoveAt(index);
            profile = settings.Profiles[Math.Max(0, index - 1)];
            settings.SelectedProfile = profile.Name;
            EnvCombo.SelectedIndex = settings.Profiles.IndexOf(profile);
            loadingUi = false;
            ProfileToUi();
            SaveSettings(false);
            AddLog("INFO", "environment " + removed + " deleted");
        }

        /// <summary>Small modal text prompt; returns null when cancelled or when the name is empty or taken.</summary>
        private string AskName(string title, string prompt, string initial)
        {
            Window dlg = new Window
            {
                Title = title, Owner = Window, Width = 380, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
                Background = Hex("#FFFFFF"), FontFamily = Window.FontFamily, FontSize = 13, ShowInTaskbar = false,
            };
            StackPanel panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            TextBox box = new TextBox { Text = initial };
            panel.Children.Add(box);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Button cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)Window.FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
            Button ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Window.FindResource("PrimaryBtn"), MinWidth = 80 };
            ok.Click += delegate { dlg.DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);
            dlg.Content = panel;
            dlg.Resources.MergedDictionaries.Add(Window.Resources);
            dlg.Loaded += delegate { box.Focus(); box.SelectAll(); };
            if (dlg.ShowDialog() != true) return null;
            string name = box.Text.Trim();
            if (name.Length == 0) return null;
            if (settings.Profiles.Exists(p => p != profile && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Toast("An environment called " + name + " already exists.");
                return null;
            }
            return name;
        }

        private void OnEnvChanged()
        {
            if (loadingUi || EnvCombo.SelectedIndex < 0) return;
            UiToProfile();
            EnvProfile next = settings.Profiles[EnvCombo.SelectedIndex];
            if (next == profile) return;
            if (terminal.State != TerminalState.Disconnected)
            {
                terminal.Disconnect();
                AddLog("WS", "virtual terminal disconnected (environment changed)");
            }
            profile = next;
            settings.SelectedProfile = profile.Name;
            ProfileToUi();
            AddLog("INFO", "environment: " + profile.Name + "  " + profile.Server);
        }

        private void SaveSettings(bool announce)
        {
            try
            {
                UiToProfile();
                settings.SelectedProfile = profile.Name;
                settings.Amount = AmountBox.Text.Trim();
                long next;
                if (long.TryParse(RefBox.Text.Trim(), out next) && IsNewRefType(SelectedType())) settings.NextEcrRefNo = next;
                settings.AutoIncrementRef = AutoRefCheck.IsChecked == true;
                settings.WaitingTimeSec = ParseInt(WaitBox.Text, 60);
                settings.RetryEnabled = RetryCheck.IsChecked == true;
                settings.RetryCode = RetryCodeBox.Text.Trim();
                settings.RetryStep = RetryStepBox.Text.Trim();
                settings.RetryMinAmount = RetryMinBox.Text.Trim();
                settings.TerminalModern = ModernCheck.IsChecked == true;
                settings.TerminalAutoReconnect = AutoReconnectCheck.IsChecked == true;
                settings.TerminalCountry = CountryBox.Text.Trim();
                settings.TerminalMode = ModeCombo.SelectedItem as string;
                settings.DeclineCode = DeclineCodeCombo.Text.Trim();
                settings.DeclineDesc = DeclineDescBox.Text.Trim();
                settings.AnswerDelaySec = ParseDouble(DelayBox.Text, 0);
                settings.BoonzAutoPreauth = BoonzCheck.IsChecked == true;
                settings.PretendDoorOpened = PretendCheck.IsChecked == true;
                settings.BoonzPreauthAmount = PreauthAmountBox.Text.Trim();
                settings.BoonzPreauthDelaySec = ParseDouble(PreauthDelayBox.Text, 1);
                settings.LockNum = ParseInt(LockBox.Text, 1);
                settings.MockMerchantPort = ParseInt(MockPortBox.Text, 8790);
                settings.MockMerchantReply = MockReplyCombo.SelectedItem as string;
                settings.MockMerchantSecret = MockSecretBox.Password;
                settings.Save();
                if (announce) AddLog("OK", "settings saved (passwords, secrets and tokens encrypted for your Windows user)");
            }
            catch (Exception e)
            {
                AddLog("ERR", "settings not saved: " + e.Message);
            }
        }

        private IntegrationApi Api()
        {
            UiToProfile();
            return new IntegrationApi(profile);
        }

        // =========================================================== token

        private async Task GetToken()
        {
            UiToProfile();
            if (string.IsNullOrEmpty(profile.SecretId))
            {
                Toast("Fill the client secret (clientIdSecretKey) first.");
                return;
            }
            GetTokenBtn.IsEnabled = false;
            try
            {
                AddLog("HTTP", "POST /access-token/application-token");
                ApiResult r = await Api().ApplicationToken();
                string message = Json.Str(r.Json, "message");
                string valid = Json.Str(r.Json, "valid");
                if (r.Ok && message != null && message.StartsWith("Bearer ") && valid != "False" && valid != "false")
                {
                    TokenBox.Text = message;
                    UiToProfile();
                    SaveSettings(false);
                    AddLog("OK", "new token saved for " + profile.Name);
                    ShowResult(r, "Application token", Outcome.Approved, "Token received", "Saved for " + profile.Name + ".");
                }
                else
                {
                    AddLog("ERR", "token request failed: " + (r.Error ?? ("HTTP " + r.HttpStatus + " " + message)));
                    ShowResult(r, "Application token", Outcome.Error, "Token request failed", r.Error ?? message ?? ("HTTP " + r.HttpStatus));
                }
            }
            finally
            {
                GetTokenBtn.IsEnabled = true;
            }
        }

        private void UpdateTokenUi()
        {
            string token = TokenBox.Text.Trim();
            if (token.Length == 0)
            {
                TokenDot.Fill = Hex("#94A3B8");
                TokenChip.Text = "No token";
                TokenInfo.Text = "Requests are sent without Authorization.";
                return;
            }
            DateTime? exp = JwtExpiry(token);
            if (exp == null)
            {
                TokenDot.Fill = Hex("#FBBF24");
                TokenChip.Text = "Token set";
                TokenInfo.Text = "Not a readable JWT; it is sent as is.";
            }
            else if (exp.Value < DateTime.Now)
            {
                TokenDot.Fill = Hex("#F87171");
                TokenChip.Text = "Token expired";
                TokenInfo.Text = "Expired " + exp.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ". Get a new one.";
            }
            else
            {
                TokenDot.Fill = Hex("#34D399");
                TokenChip.Text = "Token valid";
                TokenInfo.Text = "Valid until " + exp.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
        }

        private static DateTime? JwtExpiry(string token)
        {
            try
            {
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token.Substring(7);
                string[] parts = token.Split('.');
                if (parts.Length < 2) return null;
                string p = parts[1].Replace('-', '+').Replace('_', '/');
                while (p.Length % 4 != 0) p += "=";
                Dictionary<string, object> payload = Json.TryParseObject(Encoding.UTF8.GetString(Convert.FromBase64String(p)));
                string exp = Json.Str(payload, "exp");
                long seconds;
                if (exp == null || !long.TryParse(exp, out seconds)) return null;
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
            }
            catch
            {
                return null;
            }
        }

        // =========================================================== transaction

        private string SelectedType()
        {
            if (TypePreauth.IsChecked == true) return "PREAUTH";
            if (TypeIncremental.IsChecked == true) return "INCREMENTAL_PREAUTH";
            if (TypeCompletion.IsChecked == true) return "COMPLETION";
            if (TypeVoid.IsChecked == true) return "VOID";
            if (TypeSettle.IsChecked == true) return "SETTLE";
            return "SALE";
        }

        private static bool IsNewRefType(string type)
        {
            return type == "SALE" || type == "PREAUTH";
        }

        private static string Friendly(string type)
        {
            switch (type)
            {
                case "PREAUTH": return "pre-auth";
                case "INCREMENTAL_PREAUTH": return "incremental";
                case "COMPLETION": return "completion";
                case "VOID": return "void";
                case "SETTLE": return "settlement";
                default: return "sale";
            }
        }

        private void OnTypeChanged()
        {
            if (TypeHint == null) return;
            string type = SelectedType();
            bool money = type != "VOID" && type != "SETTLE";
            AmountPanel.IsEnabled = money;
            CurrencyPanel.IsEnabled = money;
            TipsPanel.IsEnabled = money;
            StanPanel.IsEnabled = type == "VOID";
            RefBox.IsEnabled = type != "SETTLE";
            SendBtnText.Text = "Send " + Friendly(type);
            switch (type)
            {
                case "SALE": TypeHint.Text = "Card payment: the terminal shows the amount and waits for a card."; break;
                case "PREAUTH": TypeHint.Text = "Holds the amount on the card (the Boonz fridge flow uses this)."; break;
                case "INCREMENTAL_PREAUTH": TypeHint.Text = "Adds to an approved pre-auth. Uses the same ECR reference as the pre-auth."; break;
                case "COMPLETION": TypeHint.Text = "Captures an approved pre-auth. Uses the same ECR reference as the pre-auth."; break;
                case "VOID": TypeHint.Text = "Cancels a transaction by ECR reference and STAN (filled in from the last approved reply)."; break;
                default: TypeHint.Text = "Closes the batch on the terminal."; break;
            }
            if (loadingUi) return;
            if (IsNewRefType(type))
            {
                if (AutoRefCheck.IsChecked == true) RefBox.Text = settings.NextEcrRefNo.ToString(CultureInfo.InvariantCulture);
            }
            else if (type != "SETTLE" && lastRef != null)
            {
                RefBox.Text = lastRef;
            }
            if (type == "VOID" && lastStan != null) StanBox.Text = lastStan;
        }

        private async Task SendTransaction()
        {
            if (busy) return;
            UiToProfile();
            string type = SelectedType();
            if (!CheckCredentials()) return;
            string amount = AmountBox.Text.Trim();
            string currency = CurrencyCombo.Text.Trim();
            string ecrRef = RefBox.Text.Trim();
            if (type != "VOID" && type != "SETTLE" && !IsDigits(amount))
            {
                Toast("Amount must be digits only (minor units, e.g. 100 = 1.00).");
                return;
            }
            if (type != "SETTLE" && ecrRef.Length == 0)
            {
                Toast("Fill the ECR reference.");
                return;
            }
            if (type == "VOID" && StanBox.Text.Trim().Length == 0)
            {
                Toast("A void needs the STAN of the transaction to cancel.");
                return;
            }
            if (!ConfirmProduction(Friendly(type) + (type == "VOID" || type == "SETTLE" ? "" : " of " + amount + " " + currency))) return;

            if (IsNewRefType(type))
            {
                lastRef = ecrRef;
                long n;
                if (AutoRefCheck.IsChecked == true && long.TryParse(ecrRef, out n))
                {
                    settings.NextEcrRefNo = n + 1;
                }
            }

            bool retry = RetryCheck.IsChecked == true && (type == "PREAUTH" || type == "INCREMENTAL_PREAUTH");
            string retryCode = RetryCodeBox.Text.Trim();
            long step = ParseLong(RetryStepBox.Text, 0);
            long min = ParseLong(RetryMinBox.Text, 1);

            while (true)
            {
                ApiResult r = await RunTransaction(type, amount, currency, ecrRef, StanBox.Text.Trim(), TipsBox.Text.Trim(), Friendly(type));
                if (r == null) break;
                if (!retry || r.Code != retryCode || step <= 0) break;
                long next = ParseLong(amount, 0) - step;
                if (next < min)
                {
                    AddLog("WARN", "retry stopped: " + next + " is below the minimum " + min);
                    break;
                }
                AddLog("INFO", "code " + retryCode + ": asking again for " + next + " (was " + amount + ")");
                amount = next.ToString(CultureInfo.InvariantCulture);
                AmountBox.Text = amount;
                await Task.Delay(2000);
            }

            if (IsNewRefType(SelectedType()) && AutoRefCheck.IsChecked == true)
                RefBox.Text = settings.NextEcrRefNo.ToString(CultureInfo.InvariantCulture);
            SaveSettings(false);
        }

        /// <summary>Sends one transaction and shows the reply. Returns null when it was not sent.</summary>
        private async Task<ApiResult> RunTransaction(string type, string amount, string currency, string ecrRef, string stan, string tips, string label)
        {
            int waitSec = Math.Max(1, ParseInt(WaitBox.Text, 60));
            Dictionary<string, object> body = IntegrationApi.TransactionBody(profile, type, amount, currency, ecrRef, stan, tips, waitSec * 1000);
            IntegrationApi api = Api();
            RequestBox.Text = "POST " + profile.Server.TrimEnd('/') + "/integration/transaction\n\n" + Json.Pretty(Json.Mask(Json.Serialize(body)));
            AddLog("HTTP", "POST /integration/transaction " + type + (type == "SETTLE" ? "" : " ref " + ecrRef) +
                (type == "VOID" || type == "SETTLE" ? "" : " amount " + amount + " " + currency) + " (wait " + waitSec + " s)");
            SetBusy(true, waitSec);
            ApiResult r;
            try
            {
                r = await api.Transaction(body, waitSec * 1000);
            }
            finally
            {
                SetBusy(false, 0);
            }
            Outcome outcome = ResponseCodes.Classify(r);
            ShowResult(r, label, outcome, null, null);
            string code = r.Code;
            if (outcome == Outcome.Approved)
            {
                string s = Json.Str(r.Json, "systemTraceNumber");
                string b = Json.Str(r.Json, "batchNumber");
                if (!string.IsNullOrEmpty(s)) lastStan = s;
                if (!string.IsNullOrEmpty(b)) lastBatch = b;
            }
            history.Insert(0, new HistoryRow
            {
                Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Type = type,
                Ref = type == "SETTLE" ? "" : ecrRef,
                Amount = type == "VOID" || type == "SETTLE" ? "" : amount + " " + currency,
                Code = r.Error != null ? "ERR" : code,
                Desc = r.Error ?? Json.Str(r.Json, "responseDesc") ?? ("HTTP " + r.HttpStatus),
                Stan = Json.Str(r.Json, "systemTraceNumber"),
                Ms = r.ElapsedMs.ToString(CultureInfo.InvariantCulture),
            });
            AddLog(outcome == Outcome.Approved ? "OK" : outcome == Outcome.Pending ? "WARN" : "ERR",
                type + " -> " + (r.Error ?? (code + " " + Json.Str(r.Json, "responseDesc"))) + "  (" + r.ElapsedMs + " ms)");
            return r;
        }

        private async Task CheckTrx(bool fromPos)
        {
            UiToProfile();
            string ecrRef = RefBox.Text.Trim();
            if (string.IsNullOrEmpty(profile.TerminalId) || ecrRef.Length == 0)
            {
                Toast("Needs the terminal id and the ECR reference to look up.");
                return;
            }
            string label = fromPos ? "POS check" : "Host check";
            AddLog("HTTP", "GET /integration/" + (fromPos ? "pos-trx-info" : "trx-info") + " ref " + ecrRef);
            TrxInfoBtn.IsEnabled = PosTrxInfoBtn.IsEnabled = false;
            try
            {
                IntegrationApi api = Api();
                ApiResult r = fromPos ? await api.PosTrxInfo(ecrRef) : await api.TrxInfo(ecrRef, lastBatch);
                RequestBox.Text = "GET " + r.Url;
                Outcome o = ResponseCodes.Classify(r);
                if (r.Code == "5003") o = Outcome.Declined;
                ShowResult(r, label, o, null, null);
                AddLog(o == Outcome.Approved ? "OK" : "WARN", label + " ref " + ecrRef + " -> " + (r.Error ?? (r.Code + " " + r.Desc)));
            }
            finally
            {
                TrxInfoBtn.IsEnabled = PosTrxInfoBtn.IsEnabled = true;
            }
        }

        private void CopyCurl()
        {
            UiToProfile();
            string type = SelectedType();
            int waitSec = ParseInt(WaitBox.Text, 60);
            Dictionary<string, object> body = IntegrationApi.TransactionBody(profile, type, AmountBox.Text.Trim(), CurrencyCombo.Text.Trim(),
                RefBox.Text.Trim(), StanBox.Text.Trim(), TipsBox.Text.Trim(), waitSec * 1000);
            string curl = Api().Curl("POST", "/integration/transaction", body);
            try { Clipboard.SetText(curl); AddLog("INFO", "cURL copied (password and token replaced by placeholders)"); }
            catch (Exception e) { AddLog("ERR", "clipboard: " + e.Message); }
        }

        private void SetBusy(bool on, int waitSec)
        {
            busy = on;
            SendBtn.IsEnabled = !on;
            WaitPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                waitStarted = DateTime.Now;
                waitTotalSec = waitSec;
                WaitText.Text = "Waiting for the terminal...";
                waitTimer.Start();
            }
            else
            {
                waitTimer.Stop();
            }
        }

        // =========================================================== result card

        private void ShowResult(ApiResult r, string label, Outcome outcome, string title, string desc)
        {
            string badge, back, fore, heading;
            switch (outcome)
            {
                case Outcome.Approved: badge = "✓"; back = "#D1FAE5"; fore = "#059669"; heading = "Approved"; break;
                case Outcome.Declined: badge = "✕"; back = "#FEE2E2"; fore = "#DC2626"; heading = "Declined"; break;
                case Outcome.Pending: badge = "⌛"; back = "#FEF3C7"; fore = "#D97706"; heading = "Sent, no answer in time"; break;
                default: badge = "!"; back = "#FFEDD5"; fore = "#EA580C"; heading = "Failed"; break;
            }
            ResultBadge.Background = Hex(back);
            ResultBadgeText.Foreground = Hex(fore);
            ResultBadgeText.Text = badge;
            ResultTitle.Text = (title ?? heading) + "  ·  " + label;
            ResultTitle.Foreground = Hex(fore);

            string code = r.Code;
            if (desc != null) ResultDesc.Text = desc;
            else if (r.Error != null) ResultDesc.Text = r.Error;
            else if (code != null) ResultDesc.Text = code + "  " + (r.Desc ?? "");
            else ResultDesc.Text = "HTTP " + r.HttpStatus;
            ResultExplain.Text = r.Error != null ? "The server did not answer. Check the server URL, the network/VPN and the token."
                : code != null ? ResponseCodes.Explain(code)
                : r.HttpStatus == 401 || r.HttpStatus == 403 ? "Not authorised: get a new token."
                : "";

            FactCode.Text = code ?? "-";
            FactRef.Text = Json.Str(r.Json, "ecrRefNo") ?? "-";
            FactStan.Text = Json.Str(r.Json, "systemTraceNumber") ?? "-";
            FactBatch.Text = Json.Str(r.Json, "batchNumber") ?? "-";
            FactHttp.Text = r.HttpStatus == 0 ? "-" : r.HttpStatus.ToString(CultureInfo.InvariantCulture);
            FactTime.Text = r.ElapsedMs < 1000 ? r.ElapsedMs + " ms" : (r.ElapsedMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
            ResponseBox.Text = r.Error != null ? r.Error : Json.Pretty(Json.Mask(r.ResponseBody ?? ""));
            if (r.RequestBody != null && !RequestBox.Text.StartsWith(r.Method + " " + r.Url))
                RequestBox.Text = r.Method + " " + r.Url + "\n\n" + Json.Pretty(Json.Mask(r.RequestBody));
        }

        // =========================================================== virtual terminal

        private void ToggleTerminal()
        {
            if (terminal.State != TerminalState.Disconnected)
            {
                terminal.Disconnect();
                AddLog("WS", "virtual terminal disconnected");
                return;
            }
            UiToProfile();
            if (string.IsNullOrEmpty(profile.WsUrl) || string.IsNullOrEmpty(profile.TerminalId) || string.IsNullOrEmpty(profile.Pass))
            {
                Toast("Needs the WebSocket URL, terminal id and terminal password.");
                return;
            }
            if (profile.Name != "Local")
            {
                MessageBoxResult ok = MessageBox.Show(Window,
                    "The virtual terminal logs in as " + profile.TerminalId + " on " + profile.Name + ".\n\n" +
                    "If a real POS uses this terminal id, the server drops that device's session and sends its transactions here instead.\n\nContinue?",
                    "Virtual terminal", MessageBoxButton.YesNo, profile.IsProduction ? MessageBoxImage.Warning : MessageBoxImage.Question);
                if (ok != MessageBoxResult.Yes) return;
            }
            terminal.Url = profile.WsUrl;
            terminal.TerminalId = profile.TerminalId;
            terminal.Pass = profile.Pass;
            terminal.Country = CountryBox.Text.Trim();
            terminal.Modern = ModernCheck.IsChecked == true;
            terminal.AutoReconnect = AutoReconnectCheck.IsChecked == true;
            terminal.Connect();
        }

        private void UpdateTerminalUi(TerminalState s)
        {
            string color, text, sub;
            switch (s)
            {
                case TerminalState.Connecting: color = "#FBBF24"; text = "Connecting"; sub = terminal.Url; break;
                case TerminalState.Connected: color = "#60A5FA"; text = "Logging in"; sub = "Login sent as " + terminal.TerminalId; break;
                case TerminalState.Registered: color = "#10B981"; text = "Online as " + terminal.TerminalId; sub = terminal.Modern ? "New protocol, heartbeat " + VirtualTerminal.HeartbeatSec + " s" : "Old protocol (server pings)"; break;
                default: color = "#CBD5E1"; text = "Offline"; sub = "Not connected"; break;
            }
            TermStateDot.Fill = Hex(color);
            TermDot.Fill = Hex(s == TerminalState.Disconnected ? "#94A3B8" : color);
            TermStateText.Text = text;
            TermStateSub.Text = sub ?? "";
            TermChip.Text = s == TerminalState.Registered ? "Terminal online" : s == TerminalState.Disconnected ? "Terminal offline" : "Terminal " + text.ToLowerInvariant();
            bool off = s == TerminalState.Disconnected;
            ConnectBtn.Content = off ? "Connect" : "Disconnect";
            ConnectBtn.Style = (Style)Window.FindResource(off ? "SuccessBtn" : "DangerBtn");
            DropBtn.IsEnabled = !off;
            OpenDoorWsBtn.IsEnabled = s == TerminalState.Registered || PretendCheck.IsChecked == true;
            if (off) pending.Clear();
        }

        private void UpdateModeUi()
        {
            string mode = ModeCombo.SelectedItem as string ?? "";
            DeclineRow.Visibility = mode == "Decline" || mode == "Ask me" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void OnTerminalRequest(IncomingRequest req)
        {
            string mode = ModeCombo.SelectedItem as string ?? "Approve";
            double delay = ParseDouble(DelayBox.Text, 0);
            bool twice = DuplicateCheck.IsChecked == true;
            string declineCode = DeclineCodeCombo.Text.Trim();
            string declineDesc = DeclineDescBox.Text.Trim();
            AddLog("POS", "terminal received " + req.Summary + "  [" + mode + "]");
            try
            {
                if (mode.StartsWith("Ask"))
                {
                    pending.Add(req);
                    Window.Activate();
                    return;
                }
                if (mode.StartsWith("Don't"))
                {
                    AddLog("POS", "not answering " + req.Type + " (the merchant call ends with code 1 after waitingTime)");
                    return;
                }
                if (mode.StartsWith("Busy"))
                {
                    await terminal.Busy(req);
                    return;
                }
                if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay));
                if (mode == "Decline") await terminal.Decline(req, declineCode, declineDesc, twice);
                else await terminal.Approve(req, twice);
            }
            catch (Exception e)
            {
                AddLog("ERR", "result not sent: " + e.Message);
            }
        }

        private async void OnPendingClick(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            if (b == null) return;
            string id = b.Tag as string;
            IncomingRequest req = null;
            foreach (IncomingRequest p in pending) if (p.RequestId == id) req = p;
            if (req == null) return;
            pending.Remove(req);
            string action = b.Content as string;
            try
            {
                if (action == "Approve") await terminal.Approve(req, DuplicateCheck.IsChecked == true);
                else if (action == "Decline") await terminal.Decline(req, DeclineCodeCombo.Text.Trim(), DeclineDescBox.Text.Trim(), DuplicateCheck.IsChecked == true);
                else if (action == "Busy") await terminal.Busy(req);
                else AddLog("POS", "ignored " + req.Summary);
            }
            catch (Exception ex)
            {
                AddLog("ERR", "result not sent: " + ex.Message);
            }
        }

        // =========================================================== door / Boonz

        private async Task OpenDoorApi()
        {
            UiToProfile();
            int lockNum = ParseInt(LockBox.Text, 1);
            if (PretendCheck.IsChecked == true)
            {
                AddLog("DOOR", "pretending lock " + lockNum + " opened (door API not called)");
                TriggerBoonzPreauth("door opened (pretend)", null);
                return;
            }
            if (!CheckCredentials()) return;
            if (!ConfirmProduction("open door " + lockNum)) return;
            OpenDoorApiBtn.IsEnabled = false;
            try
            {
                AddLog("HTTP", "POST /integration/door/open lock " + lockNum);
                ApiResult r = await Api().OpenDoor(lockNum, DoorRefBox.Text.Trim());
                Outcome o = ResponseCodes.Classify(r);
                ShowResult(r, "Open door", o, o == Outcome.Approved ? "Door opened" : null, null);
                AddLog(o == Outcome.Approved ? "OK" : "ERR", "door/open -> " + (r.Error ?? (r.Code + " " + r.Desc)));
                if (o == Outcome.Approved) TriggerBoonzPreauth("door API answered 0", null);
            }
            finally
            {
                OpenDoorApiBtn.IsEnabled = true;
            }
        }

        private async Task PressStart()
        {
            int lockNum = ParseInt(LockBox.Text, 1);
            if (PretendCheck.IsChecked == true)
            {
                AddLog("DOOR", "START pressed, pretending lock " + lockNum + " opened (no OpenDoor sent)");
                TriggerBoonzPreauth("door opened (pretend)", null);
                return;
            }
            if (terminal.State != TerminalState.Registered)
            {
                Toast("Connect the virtual terminal first.");
                return;
            }
            try
            {
                await terminal.SendOpenDoor(lockNum, DoorRefBox.Text.Trim());
                AddLog("DOOR", "START pressed on the virtual terminal: OpenDoor sent, waiting for DoorResult");
            }
            catch (Exception e)
            {
                AddLog("ERR", "OpenDoor not sent: " + e.Message);
            }
        }

        private void OnDoorResult(string code, string desc)
        {
            AddLog(code == "0" ? "OK" : "ERR", "DoorResult " + code + " " + desc + (code == "0" ? "" : "  (" + ResponseCodes.Explain(code) + ")"));
            if (code == "0") TriggerBoonzPreauth("DoorResult 0", null);
        }

        private void OnMockDoorOpened(string terminalId, int lockNum)
        {
            AddLog("DOOR", "merchant (mock) opened lock " + lockNum + " for " + terminalId);
            TriggerBoonzPreauth("mock Tap Start opened the door", terminalId);
        }

        /// <summary>
        /// Boonz behaviour: once the door is open, the merchant sends a PREAUTH to the terminal. One door
        /// opening can be reported twice (mock Tap Start, then the door API / DoorResult), so a second trigger
        /// within 20 s is skipped.
        /// </summary>
        private async void TriggerBoonzPreauth(string reason, string terminalId)
        {
            if (BoonzCheck.IsChecked != true)
            {
                AddLog("DOOR", reason + ". Pre-auth not sent: \"act as Boonz\" is off.");
                return;
            }
            if ((DateTime.Now - lastBoonzTrigger).TotalSeconds < 20)
            {
                AddLog("DOOR", reason + ": pre-auth already sent for this door opening, skipped.");
                return;
            }
            lastBoonzTrigger = DateTime.Now;
            UiToProfile();
            string amount = PreauthAmountBox.Text.Trim();
            if (!IsDigits(amount))
            {
                AddLog("ERR", "Boonz pre-auth amount must be digits (minor units).");
                return;
            }
            if (busy)
            {
                AddLog("WARN", "a transaction is still waiting; Boonz pre-auth not sent.");
                return;
            }
            double delay = ParseDouble(PreauthDelayBox.Text, 1);
            AddLog("DOOR", reason + ": Boonz sends PREAUTH " + amount + " " + CurrencyCombo.Text.Trim() + (delay > 0 ? " in " + Num(delay) + " s" : ""));
            if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay));

            EnvProfile saved = profile;
            if (!string.IsNullOrEmpty(terminalId) && terminalId != profile.TerminalId)
            {
                AddLog("WARN", "Tap Start was for " + terminalId + ", the pre-auth goes to that terminal with this profile's password");
                profile = profile.Copy();
                profile.TerminalId = terminalId;
            }
            try
            {
                string ecrRef = settings.NextEcrRefNo.ToString(CultureInfo.InvariantCulture);
                settings.NextEcrRefNo++;
                lastRef = ecrRef;
                if (IsNewRefType(SelectedType()) && AutoRefCheck.IsChecked == true) RefBox.Text = settings.NextEcrRefNo.ToString(CultureInfo.InvariantCulture);
                await RunTransaction("PREAUTH", amount, CurrencyCombo.Text.Trim(), ecrRef, "", "", "Boonz pre-auth");
            }
            finally
            {
                profile = saved;
                SaveSettings(false);
            }
        }

        private void ToggleMock()
        {
            if (mock.Running)
            {
                mock.Stop();
                UpdateMockUi();
                return;
            }
            int port = ParseInt(MockPortBox.Text, 0);
            if (port <= 0 || port > 65535)
            {
                Toast("Enter a port between 1 and 65535.");
                return;
            }
            mock.ReplyMode = MockReplyCombo.SelectedItem as string;
            mock.Secret = MockSecretBox.Password;
            try
            {
                mock.Start(port);
            }
            catch (Exception e)
            {
                AddLog("ERR", "mock Tap Start could not listen on port " + port + ": " + e.Message);
                Toast("Port " + port + " is not available: " + e.Message);
            }
            UpdateMockUi();
        }

        private void UpdateMockUi()
        {
            if (MockConfigBox == null) return;
            string port = mock.Running ? mock.Port.ToString(CultureInfo.InvariantCulture) : MockPortBox.Text.Trim();
            MockConfigBox.Text = "merchant.ecr.mock-enabled=false\nmerchant.ecr.url=http://localhost:" + port;
            MockToggleBtn.Content = mock.Running ? "Stop" : "Start";
            MockToggleBtn.Style = (Style)Window.FindResource(mock.Running ? "DangerBtn" : "SuccessBtn");
            MockDot.Fill = Hex(mock.Running ? "#34D399" : "#94A3B8");
            MockChip.Text = mock.Running ? "Tap Start mock :" + port : "Tap Start mock off";
            MockPortBox.IsEnabled = !mock.Running;
        }

        // =========================================================== helpers

        private bool CheckCredentials()
        {
            if (string.IsNullOrEmpty(profile.Server) || string.IsNullOrEmpty(profile.TerminalId) || string.IsNullOrEmpty(profile.Pass))
            {
                Toast("Fill the server, terminal id and terminal password.");
                return false;
            }
            return true;
        }

        private bool ConfirmProduction(string what)
        {
            if (!profile.IsProduction) return true;
            return MessageBox.Show(Window, "PRODUCTION (" + profile.Name + ")\n\nSend " + what + " to terminal " + profile.TerminalId + "?\nThis is a real terminal and real money.",
                "Production", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private void Toast(string text)
        {
            MessageBox.Show(Window, text, "Wireless ECR Simulator", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(Window,
                "Wireless ECR Simulator " + Version + "\nby Ahmad El Lahib\n\n" +
                "Merchant side: token, sale, pre-auth, incremental, completion, void, settlement, trx-info, pos-trx-info, door open.\n" +
                "Terminal side: a virtual POS on the WebSocket (old and new protocol) that approves, declines, stays busy or stays silent.\n" +
                "Door: a mock merchant Tap Start API, and Boonz behaviour (pre-auth after the door opens).\n\n" +
                "Settings: " + AppSettings.FilePath + "\nLogs: " + System.IO.Path.GetDirectoryName(logFile),
                "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CopyLog()
        {
            StringBuilder sb = new StringBuilder();
            foreach (LogEntry e in logs) sb.Append(e.Time).Append("  ").Append(e.Tag.PadRight(5)).Append(' ').AppendLine(e.Text);
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        private void AddLog(string tag, string text)
        {
            DateTime now = DateTime.Now;
            string time = now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            lock (logFileLock)
            {
                try { File.AppendAllText(logFile, now.ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture) + time + "  " + tag.PadRight(5) + " " + text + Environment.NewLine, Encoding.UTF8); }
                catch { }
            }
            Ui(() =>
            {
                string back, fore;
                switch (tag)
                {
                    case "IN": back = "#DBEAFE"; fore = "#1D4ED8"; break;
                    case "OUT": back = "#EDE9FE"; fore = "#6D28D9"; break;
                    case "OK": back = "#D1FAE5"; fore = "#047857"; break;
                    case "ERR": back = "#FEE2E2"; fore = "#B91C1C"; break;
                    case "WARN": back = "#FEF3C7"; fore = "#B45309"; break;
                    case "HTTP": back = "#E0E7FF"; fore = "#4338CA"; break;
                    case "DOOR": back = "#CCFBF1"; fore = "#0F766E"; break;
                    case "POS": back = "#FCE7F3"; fore = "#BE185D"; break;
                    default: back = "#F1F5F9"; fore = "#475569"; break;
                }
                logs.Add(new LogEntry { Time = time, Tag = tag, Text = text, TagBack = Hex(back), TagFore = Hex(fore) });
                while (logs.Count > MaxLogLines) logs.RemoveAt(0);
                LogList.ScrollIntoView(logs[logs.Count - 1]);
            });
        }

        private void Ui(Action a)
        {
            if (Window.Dispatcher.CheckAccess()) a();
            else Window.Dispatcher.BeginInvoke(a);
        }

        private static readonly Dictionary<string, SolidColorBrush> brushes = new Dictionary<string, SolidColorBrush>();

        private static SolidColorBrush Hex(string hex)
        {
            SolidColorBrush b;
            if (!brushes.TryGetValue(hex, out b))
            {
                b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                brushes[hex] = b;
            }
            return b;
        }

        private static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static long ParseLong(string s, long fallback)
        {
            long v;
            return long.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static double ParseDouble(string s, double fallback)
        {
            double v;
            return double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static string Num(double d)
        {
            return d.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
