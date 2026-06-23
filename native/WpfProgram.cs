using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

internal static class WpfProgram
{
    internal static string AppRoot
    {
        get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
    }

    [STAThread]
    private static void Main()
    {
        try
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                Log("dispatcher-fatal:" + e.Exception);
                e.Handled = true;
            };
            PikaWindow window = new PikaWindow();
            app.Run(window);
        }
        catch (Exception ex)
        {
            Log("fatal:" + ex);
        }
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppRoot, "pika-wpf.log"),
                "[" + DateTime.Now.ToString("s") + "] " + message + Environment.NewLine);
        }
        catch
        {
        }
    }
}

internal enum PikaState
{
    Idle,
    Happy,
    Touch,
    Thinking,
    Talking,
    Sleepy
}

internal sealed class PikaWindow : Window
{
    private readonly Grid root;
    private readonly Border chat;
    private StackPanel history;
    private ScrollViewer historyScroll;
    private TextBox input;
    private Button send;
    private Button crisis;
    private TextBlock connection;
    private MenuItem historyToggleItem;
    private readonly Canvas petStage;
    private Image petImage;
    private Canvas effects;
    private Border stateBadge;
    private TextBlock stateBadgeText;
    private readonly DispatcherTimer idleTimer;
    private readonly DispatcherTimer ambientTimer;
    private readonly Random random = new Random();
    private readonly List<ChatEntry> conversation = new List<ChatEntry>();
    private readonly RiskState riskState = new RiskState();
    private readonly Dictionary<PikaState, BitmapImage> stateImages = new Dictionary<PikaState, BitmapImage>();
    private readonly ScaleTransform petScale = new ScaleTransform(1, 1);
    private readonly RotateTransform petRotate = new RotateTransform(0);
    private readonly TranslateTransform petMove = new TranslateTransform(0, 0);
    private PikaState state = PikaState.Idle;
    private DateTime lastInteraction = DateTime.Now;
    private bool chinese = true;
    private bool sending;
    private bool dragging;
    private bool saveHistoryEnabled;
    private int stateVersion;
    private DateTime previewLockedUntil = DateTime.MinValue;
    private Point dragStart;
    private Point windowStart;
    private BitmapImage defaultPetImage;

    private string DataRoot
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PikaDesktopPet"); }
    }

    private string SettingsPath
    {
        get { return Path.Combine(DataRoot, "settings.ini"); }
    }

    private string HistoryPath
    {
        get { return Path.Combine(DataRoot, "chat-history.txt"); }
    }

    private string LegacyHistoryPath
    {
        get { return Path.Combine(WpfProgram.AppRoot, "chat-history.txt"); }
    }

    public PikaWindow()
    {
        Title = "Pika Desktop Pet";
        Width = 390;
        Height = 650;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Left = SystemParameters.WorkArea.Right - Width - 18;
        Top = SystemParameters.WorkArea.Bottom - Height - 12;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        root = new Grid();
        root.Background = Brushes.Transparent;
        Content = root;

        chat = BuildChat();
        root.Children.Add(chat);

        petStage = BuildPetStage();
        root.Children.Add(petStage);

        LoadSettings();
        ContextMenu = BuildContextMenu();
        LoadHistory();
        LoadPikachu();

        idleTimer = new DispatcherTimer();
        idleTimer.Interval = TimeSpan.FromSeconds(2);
        idleTimer.Tick += delegate
        {
            if (!sending && DateTime.Now - lastInteraction > TimeSpan.FromSeconds(45) && state != PikaState.Sleepy)
                SetState(PikaState.Sleepy, false);
        };
        idleTimer.Start();

        ambientTimer = new DispatcherTimer();
        ambientTimer.Interval = TimeSpan.FromSeconds(12);
        ambientTimer.Tick += delegate
        {
            if (!sending && !dragging && chat.Visibility != Visibility.Visible &&
                DateTime.Now >= previewLockedUntil && state == PikaState.Idle)
            {
                PikaState[] ambient = { PikaState.Thinking, PikaState.Touch };
                SetState(ambient[random.Next(ambient.Length)], true);
            }
        };
        ambientTimer.Start();

        SourceInitialized += delegate
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null) source.AddHook(WindowProc);
        };

        Loaded += delegate
        {
            SetState(PikaState.Touch, true);
            WpfProgram.Log("wpf-ready");
        };
    }

    private Border BuildChat()
    {
        Border panel = new Border();
        panel.Width = 370;
        panel.Height = 400;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.Margin = new Thickness(8, 8, 0, 0);
        panel.CornerRadius = new CornerRadius(16);
        panel.BorderThickness = new Thickness(1);
        panel.BorderBrush = new SolidColorBrush(Color.FromRgb(230, 210, 152));
        panel.Background = new SolidColorBrush(Color.FromRgb(255, 253, 246));
        panel.Effect = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 4,
            Opacity = 0.22,
            Color = Color.FromRgb(70, 55, 25)
        };
        panel.Visibility = Visibility.Collapsed;

        Grid layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        panel.Child = layout;

        Grid header = new Grid { Margin = new Thickness(14, 7, 10, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock title = new TextBlock
        {
            Text = "皮卡伙伴",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(44, 39, 29))
        };
        header.Children.Add(title);

        StackPanel headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        connection = new TextBlock
        {
            Text = "AI 待机",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(112, 101, 80)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        headerActions.Children.Add(connection);
        crisis = MakeHeaderButton("求助", delegate { ShowCrisisHelp(); });
        crisis.Background = new SolidColorBrush(Color.FromRgb(255, 112, 96));
        crisis.Foreground = Brushes.White;
        headerActions.Children.Add(crisis);
        headerActions.Children.Add(MakeHeaderButton("中文", delegate { chinese = true; title.Text = "皮卡伙伴"; crisis.Content = "求助"; }));
        headerActions.Children.Add(MakeHeaderButton("EN", delegate { chinese = false; title.Text = "Pika Buddy"; crisis.Content = "Help"; }));
        headerActions.Children.Add(MakeHeaderButton("×", delegate { ToggleChat(false); }));
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        history = new StackPanel { Margin = new Thickness(10, 4, 10, 8) };
        historyScroll = new ScrollViewer
        {
            Content = history,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2)
        };
        Grid.SetRow(historyScroll, 1);
        layout.Children.Add(historyScroll);

        Grid composer = new Grid { Margin = new Thickness(12, 8, 12, 12) };
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        input = new TextBox
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 0, 10, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        input.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        };
        Border inputFrame = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 210, 190)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = input
        };
        composer.Children.Add(inputFrame);
        send = new Button
        {
            Content = "发",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 38)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        send.Click += delegate { SendMessage(); };
        RoundButton(send, 10);
        Grid.SetColumn(send, 1);
        composer.Children.Add(send);
        Grid.SetRow(composer, 2);
        layout.Children.Add(composer);

        return panel;
    }

    private Button MakeHeaderButton(string text, Action click)
    {
        Button button = new Button
        {
            Content = text,
            Height = 28,
            MinWidth = text == "×" ? 28 : 42,
            Padding = new Thickness(7, 0, 7, 0),
            Margin = new Thickness(2, 0, 0, 0),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(255, 226, 97)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        RoundButton(button, 7);
        button.Click += delegate { click(); };
        return button;
    }

    private static void RoundButton(Button button, double radius)
    {
        ControlTemplate template = new ControlTemplate(typeof(Button));
        FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        button.Template = template;
    }

    private Canvas BuildPetStage()
    {
        Canvas stage = new Canvas
        {
            Width = 260,
            Height = 225,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 2),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand
        };
        stage.MouseEnter += delegate
        {
            lastInteraction = DateTime.Now;
            if (!sending) ShowRandomInteractiveState();
        };
        stage.MouseLeftButtonDown += PetMouseDown;
        stage.MouseMove += PetMouseMove;
        stage.MouseLeftButtonUp += PetMouseUp;

        Ellipse shadow = new Ellipse
        {
            Width = 132,
            Height = 25,
            Fill = new SolidColorBrush(Color.FromArgb(48, 20, 18, 12)),
            Effect = new BlurEffect { Radius = 8 }
        };
        Canvas.SetLeft(shadow, 68);
        Canvas.SetTop(shadow, 187);
        stage.Children.Add(shadow);

        petImage = new Image
        {
            Width = 190,
            Height = 190,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.7),
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(petImage, BitmapScalingMode.Fant);
        TransformGroup transforms = new TransformGroup();
        transforms.Children.Add(petScale);
        transforms.Children.Add(petRotate);
        transforms.Children.Add(petMove);
        petImage.RenderTransform = transforms;
        Canvas.SetLeft(petImage, 34);
        Canvas.SetTop(petImage, 10);
        stage.Children.Add(petImage);

        effects = new Canvas { Width = 260, Height = 225, IsHitTestVisible = false };
        stage.Children.Add(effects);

        stateBadgeText = new TextBlock
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(69, 57, 34))
        };
        stateBadge = new Border
        {
            Child = stateBadgeText,
            Background = new SolidColorBrush(Color.FromArgb(238, 255, 246, 190)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(230, 196, 88)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 4, 9, 4),
            Opacity = 0,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(stateBadge, 92);
        Canvas.SetTop(stateBadge, 1);
        stage.Children.Add(stateBadge);
        return stage;
    }

    private ContextMenu BuildContextMenu()
    {
        ContextMenu menu = new ContextMenu();
        MenuItem chatItem = new MenuItem { Header = "打开对话" };
        chatItem.Click += delegate { ToggleChat(true); };
        menu.Items.Add(chatItem);
        MenuItem crisisItem = new MenuItem { Header = "紧急帮助 / Crisis help" };
        crisisItem.Click += delegate { ShowCrisisHelp(); };
        menu.Items.Add(crisisItem);
        historyToggleItem = new MenuItem();
        historyToggleItem.Click += delegate { ToggleHistorySaving(); };
        menu.Items.Add(historyToggleItem);
        MenuItem clearHistoryItem = new MenuItem { Header = "清空聊天历史 / Clear history" };
        clearHistoryItem.Click += delegate { ClearHistory(); };
        menu.Items.Add(clearHistoryItem);
        menu.Items.Add(new Separator());
        UpdateHistoryMenuHeader();
        MenuItem states = new MenuItem { Header = "预览六种状态" };
        AddStateMenu(states, "待机", PikaState.Idle);
        AddStateMenu(states, "开心", PikaState.Happy);
        AddStateMenu(states, "触碰", PikaState.Touch);
        AddStateMenu(states, "思考", PikaState.Thinking);
        AddStateMenu(states, "说话", PikaState.Talking);
        AddStateMenu(states, "困倦", PikaState.Sleepy);
        menu.Items.Add(states);
        menu.Items.Add(new Separator());
        MenuItem exit = new MenuItem { Header = "退出桌宠" };
        exit.Click += delegate { Close(); };
        menu.Items.Add(exit);
        return menu;
    }

    private void AddStateMenu(MenuItem parent, string label, PikaState target)
    {
        MenuItem item = new MenuItem { Header = label };
        item.Click += delegate
        {
            previewLockedUntil = DateTime.MinValue;
            SetState(target, true);
            previewLockedUntil = DateTime.Now.AddSeconds(6);
            ShowStateBadge(target, 6);
        };
        parent.Items.Add(item);
    }

    private void LoadPikachu()
    {
        string local = Path.Combine(WpfProgram.AppRoot, "assets", "pikachu.png");
        if (File.Exists(local))
        {
            LoadStateImages(local);
            return;
        }

        string fallback = FindLocalPetImage();
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            LoadStateImages(fallback);
            return;
        }

        Task.Factory.StartNew(delegate
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string url = "https://img.pokemondb.net/sprites/home/normal/pikachu.png";
                string directory = Path.GetDirectoryName(local);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                using (WebClient client = new WebClient())
                {
                    client.Headers["User-Agent"] = "PikaDesktopPet";
                    client.DownloadFile(url, local);
                }
                Dispatcher.BeginInvoke((Action)delegate { LoadStateImages(local); });
            }
            catch (Exception ex)
            {
                WpfProgram.Log("image-error:" + ex.Message);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    string retryFallback = FindLocalPetImage();
                    if (!string.IsNullOrWhiteSpace(retryFallback))
                        LoadStateImages(retryFallback);
                });
            }
        });
    }

    private string FindLocalPetImage()
    {
        string[] names =
        {
            "pikachu-happy.png",
            "pikachu-touch.png",
            "pikachu-thinking.png",
            "pikachu-talking.png",
            "pikachu-sleepy.png"
        };
        foreach (string name in names)
        {
            string path = Path.Combine(WpfProgram.AppRoot, "assets", name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private void LoadStateImages(string defaultPath)
    {
        stateImages.Clear();
        defaultPetImage = LoadBitmap(defaultPath);
        stateImages[PikaState.Idle] = defaultPetImage;

        LoadOptionalStateImage(PikaState.Happy, "pikachu-happy.png");
        LoadOptionalStateImage(PikaState.Touch, "pikachu-touch.png");
        LoadOptionalStateImage(PikaState.Thinking, "pikachu-thinking.png");
        LoadOptionalStateImage(PikaState.Talking, "pikachu-talking.png");
        LoadOptionalStateImage(PikaState.Sleepy, "pikachu-sleepy.png");

        SetImageForState(state);
    }

    private void LoadOptionalStateImage(PikaState target, string fileName)
    {
        string path = Path.Combine(WpfProgram.AppRoot, "assets", fileName);
        if (File.Exists(path))
            stateImages[target] = LoadBitmap(path);
    }

    private BitmapImage LoadBitmap(string path)
    {
        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void SetImageForState(PikaState target)
    {
        BitmapImage image;
        if (stateImages.TryGetValue(target, out image))
        {
            petImage.Source = image;
            return;
        }

        if (defaultPetImage != null)
            petImage.Source = defaultPetImage;
    }

    private bool HasStateImage(PikaState target)
    {
        return stateImages.ContainsKey(target);
    }

    private void ToggleChat(bool visible)
    {
        ToggleChat(visible, true);
    }

    private void ToggleChat(bool visible, bool showHappyReaction)
    {
        chat.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        lastInteraction = DateTime.Now;
        if (visible)
        {
            if (showHappyReaction) SetState(PikaState.Happy, true);
            input.Focus();
            ScrollToBottom();
        }
    }

    private void PetMouseDown(object sender, MouseButtonEventArgs e)
    {
        dragging = true;
        dragStart = PointToScreen(e.GetPosition(this));
        windowStart = new Point(Left, Top);
        petStage.CaptureMouse();
        e.Handled = true;
    }

    private void PetMouseMove(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        Point now = PointToScreen(e.GetPosition(this));
        Left = windowStart.X + now.X - dragStart.X;
        Top = windowStart.Y + now.Y - dragStart.Y;
    }

    private void PetMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        Point now = PointToScreen(e.GetPosition(this));
        bool moved = Math.Abs(now.X - dragStart.X) + Math.Abs(now.Y - dragStart.Y) > 5;
        dragging = false;
        petStage.ReleaseMouseCapture();
        if (!moved)
        {
            ToggleChat(chat.Visibility != Visibility.Visible, false);
            if (!sending) ShowRandomInteractiveState();
        }
        e.Handled = true;
    }

    private void ShowRandomInteractiveState()
    {
        PikaState[] candidates =
        {
            PikaState.Happy,
            PikaState.Touch,
            PikaState.Thinking,
            PikaState.Talking,
            PikaState.Sleepy
        };
        List<PikaState> available = new List<PikaState>();
        foreach (PikaState candidate in candidates)
        {
            if (candidate != state && HasStateImage(candidate))
                available.Add(candidate);
        }

        if (available.Count == 0) return;
        lastInteraction = DateTime.Now;
        PikaState next = available[random.Next(available.Count)];
        WpfProgram.Log("interactive-state:" + next);
        SetState(next, true);
    }

    private void SetState(PikaState next, bool animated)
    {
        if (DateTime.Now < previewLockedUntil && next != state) return;
        int version = ++stateVersion;
        state = next;
        if (next != PikaState.Sleepy) lastInteraction = DateTime.Now;
        ClearEffects();
        SetImageForState(next);
        petImage.BeginAnimation(UIElement.OpacityProperty, null);
        petMove.BeginAnimation(TranslateTransform.XProperty, null);
        petMove.BeginAnimation(TranslateTransform.YProperty, null);
        petRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        petScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        petScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        petMove.X = 0;
        petMove.Y = 0;
        petRotate.Angle = 0;
        petScale.ScaleX = petScale.ScaleY = 1;
        petImage.Opacity = 1;

        if (next == PikaState.Idle)
        {
            AnimateBothScale(1, 1.025, 1.8, true);
            AnimateMoveY(0, -3, 1.8, true);
        }
        else if (next == PikaState.Happy)
        {
            AnimateBothScale(1.02, 1.1, 0.34, true);
            AnimateMoveY(0, -34, 0.34, true);
            AnimateRotate(-7, 7, 0.34, true);
            AddStars();
            ShowStateBadge(next, 3);
            ReturnToIdleLater(3.0, version);
        }
        else if (next == PikaState.Touch)
        {
            petScale.ScaleX = petScale.ScaleY = 1.08;
            AnimateMoveX(-10, 10, 0.09, true);
            AnimateRotate(-6, 6, 0.09, true);
            AddSparks();
            ShowStateBadge(next, 2.2);
            ReturnToIdleLater(2.2, version);
        }
        else if (next == PikaState.Thinking)
        {
            if (HasStateImage(PikaState.Thinking))
            {
                petRotate.Angle = -4;
                petMove.X = -6;
                petMove.Y = 3;
                petScale.ScaleX = petScale.ScaleY = 0.98;
            }
            else
            {
                petRotate.Angle = -15;
                petMove.X = -22;
                petMove.Y = 12;
                petScale.ScaleX = petScale.ScaleY = 0.96;
                AddThought();
            }
            ShowStateBadge(next, sending ? 30 : 4);
            if (!sending) ReturnToIdleLater(4.0, version);
        }
        else if (next == PikaState.Talking)
        {
            petRotate.Angle = HasStateImage(PikaState.Talking) ? 2 : 5;
            AnimateBothScale(1.02, 1.1, 0.23, true);
            AnimateMoveY(0, -8, 0.23, true);
            if (!HasStateImage(PikaState.Talking)) AddSpeechDots();
            ShowStateBadge(next, 3);
            ReturnToIdleLater(3.0, version);
        }
        else if (next == PikaState.Sleepy)
        {
            if (HasStateImage(PikaState.Sleepy))
            {
                petRotate.Angle = 0;
                petMove.X = 6;
                petMove.Y = 15;
                petScale.ScaleX = petScale.ScaleY = 0.9;
                AnimateBothScale(0.9, 0.92, 2.2, true);
            }
            else
            {
                petRotate.Angle = 65;
                petMove.X = 25;
                petMove.Y = 42;
                petScale.ScaleX = petScale.ScaleY = 0.8;
                AnimateBothScale(0.8, 0.82, 2.2, true);
                AddSleep();
            }
            ShowStateBadge(next, 6);
            if (DateTime.Now >= previewLockedUntil) ReturnToIdleLater(6.0, version);
        }
    }

    private void AnimateBothScale(double from, double to, double seconds, bool reverse)
    {
        DoubleAnimation x = LoopAnimation(from, to, seconds, reverse);
        DoubleAnimation y = LoopAnimation(from, to, seconds, reverse);
        petScale.BeginAnimation(ScaleTransform.ScaleXProperty, x);
        petScale.BeginAnimation(ScaleTransform.ScaleYProperty, y);
    }

    private void AnimateMoveX(double from, double to, double seconds, bool reverse)
    {
        petMove.BeginAnimation(TranslateTransform.XProperty, LoopAnimation(from, to, seconds, reverse));
    }

    private void AnimateMoveY(double from, double to, double seconds, bool reverse)
    {
        petMove.BeginAnimation(TranslateTransform.YProperty, LoopAnimation(from, to, seconds, reverse));
    }

    private void AnimateRotate(double from, double to, double seconds, bool reverse)
    {
        petRotate.BeginAnimation(RotateTransform.AngleProperty, LoopAnimation(from, to, seconds, reverse));
    }

    private static DoubleAnimation LoopAnimation(double from, double to, double seconds, bool reverse)
    {
        return new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = reverse,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
    }

    private void ReturnToIdleLater(double seconds, int version)
    {
        DispatcherTimer once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        once.Tick += delegate
        {
            once.Stop();
            if (!sending && stateVersion == version && DateTime.Now >= previewLockedUntil)
                SetState(PikaState.Idle, true);
        };
        once.Start();
    }

    private void ShowStateBadge(PikaState target, double seconds)
    {
        string label = target == PikaState.Happy ? "开心充电" :
            target == PikaState.Touch ? "触碰放电" :
            target == PikaState.Thinking ? "认真思考" :
            target == PikaState.Talking ? "正在说话" :
            target == PikaState.Sleepy ? "困倦休息" : "待机陪伴";
        stateBadgeText.Text = label;
        stateBadge.BeginAnimation(UIElement.OpacityProperty, null);
        stateBadge.Opacity = 1;
        DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.45))
        {
            BeginTime = TimeSpan.FromSeconds(Math.Max(0.5, seconds - 0.45))
        };
        stateBadge.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void ClearEffects()
    {
        effects.Children.Clear();
    }

    private void AddStars()
    {
        AddEffectText("✦", 28, 45, 26, Color.FromRgb(255, 210, 48));
        AddEffectText("✦", 208, 68, 20, Color.FromRgb(255, 210, 48));
        AddEffectText("♡", 198, 28, 23, Color.FromRgb(255, 105, 105));
    }

    private void AddSparks()
    {
        AddEffectText("ϟ", 18, 70, 30, Color.FromRgb(255, 205, 45));
        AddEffectText("ϟ", 218, 90, 28, Color.FromRgb(255, 205, 45));
    }

    private void AddThought()
    {
        AddEffectText("●", 192, 49, 11, Colors.White);
        AddEffectText("●", 207, 30, 16, Colors.White);
        AddEffectText("?", 224, 2, 26, Color.FromRgb(90, 78, 50));
    }

    private void AddSpeechDots()
    {
        AddEffectText("●  ●  ●", 74, 2, 13, Color.FromRgb(80, 68, 42));
    }

    private void AddSleep()
    {
        AddEffectText("z", 194, 61, 19, Color.FromRgb(86, 70, 41));
        AddEffectText("Z", 213, 35, 26, Color.FromRgb(86, 70, 41));
    }

    private void AddEffectText(string text, double left, double top, double size, Color color)
    {
        TextBlock block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = size,
            Foreground = new SolidColorBrush(color),
            Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Opacity = 0.28 }
        };
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        effects.Children.Add(block);
    }

    private void SendMessage()
    {
        string text = input.Text.Trim();
        if (sending || text.Length == 0) return;
        input.Text = "";
        AddBubble("user", text);
        Border thinking = AddBubble("thinking", chinese ? "正在想一想…" : "Thinking...");
        sending = true;
        input.IsEnabled = false;
        send.IsEnabled = false;
        SetState(PikaState.Thinking, true);

        Task.Factory.StartNew(delegate
        {
            AssistantResponse response = Answer(text, chinese);
            Dispatcher.BeginInvoke((Action)delegate
            {
                history.Children.Remove(thinking);
                AddBubble("assistant", response.Content);
                conversation.Add(new ChatEntry("user", text));
                conversation.Add(new ChatEntry("assistant", response.Content));
                TrimConversation();
                SaveHistory();
                connection.Text = response.StatusText;
                connection.Foreground = new SolidColorBrush(response.StatusColor);
                sending = false;
                input.IsEnabled = true;
                send.IsEnabled = true;
                SetState(PikaState.Talking, true);
                input.Focus();
            });
        });
    }

    private AssistantResponse Answer(string text, bool useChinese)
    {
        RiskDecision decision = RiskAnalyzer.ApplyDecision(RiskAnalyzer.Analyze(text, riskState), riskState);
        if (decision.Level != RiskLevel.R0)
        {
            return new AssistantResponse(
                SafetyReply(decision, useChinese),
                false,
                true,
                decision.Level,
                useChinese ? "安全模式" : "Safety",
                Color.FromRgb(190, 74, 58));
        }

        try
        {
            string key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(key))
            {
                string answer = CallDeepSeek(key, text, useChinese);
                return new AssistantResponse(
                    answer,
                    true,
                    false,
                    decision.Level,
                    useChinese ? "AI 在线" : "AI online",
                    Color.FromRgb(55, 145, 84));
            }
        }
        catch (Exception ex)
        {
            WpfProgram.Log("deepseek-error:" + ex.Message);
        }
        return new AssistantResponse(
            LocalSupportReply(text, useChinese),
            false,
            false,
            decision.Level,
            useChinese ? "离线陪伴" : "Offline",
            Color.FromRgb(155, 112, 65));
    }

    private void ShowCrisisHelp()
    {
        ToggleChat(true, false);
        string message = CrisisHelpText(chinese);
        AddBubble("assistant", message);
        conversation.Add(new ChatEntry("assistant", message));
        TrimConversation();
        SaveHistory();
        connection.Text = chinese ? "安全入口" : "Safety";
        connection.Foreground = new SolidColorBrush(Color.FromRgb(190, 74, 58));
        SetState(PikaState.Thinking, true);
    }

    private string SafetyReply(RiskDecision decision, bool useChinese)
    {
        if (decision.Level == RiskLevel.R3)
        {
            return useChinese
                ? "这听起来已经很紧急了。请你现在立刻联系当地紧急服务，或让身边的人帮你联系。如果你在中国大陆，请拨打 110 或 120；如果在美国，请拨打 911 或 988。请先去有人在的地方，远离可能伤害自己或别人的物品和地点。"
                : "This sounds urgent. Please contact local emergency services now, or ask someone nearby to help. In the U.S., call 911 for immediate danger or call/text 988 for crisis support. Move toward another person if you can, and away from anything or anywhere that could put you or someone else in danger.";
        }

        if (decision.Level == RiskLevel.R2)
        {
            return useChinese
                ? "我很担心你现在的安全。你不需要一个人扛着这件事。请现在联系一个可信任的人，或拨打当地紧急电话/危机热线。你也可以先告诉我：你现在是一个人吗？能不能先去有人在的地方？"
                : "I am worried about your safety right now. You do not have to carry this alone. Please contact someone you trust now, or call local emergency or crisis support. Are you alone right now, and can you move somewhere with another person?";
        }

        if (riskState.SupportPersonAvailable == 1)
        {
            return useChinese
                ? "听到你这样说，我会有点担心你。既然身边有人可以联系，先让对方陪你待一会儿会更稳。这个念头现在有多强，是一闪而过，还是正在拉着你往前走？"
                : "Hearing that makes me a little worried about you. Since there is someone you can contact, please let them stay with you for a bit. How strong is this thought right now: passing through, or pulling you forward?";
        }

        return useChinese
            ? "听到你这样说，我会有点担心你现在的安全。你现在有伤害自己或结束生命的想法吗？如果有，我们先不分析原因，先一起把接下来几分钟稳住。"
            : "Hearing that makes me a little worried about your safety. Are you having thoughts of hurting yourself or ending your life right now? If yes, let us pause the analysis and focus on getting through the next few minutes safely.";
    }

    private string CrisisHelpText(bool useChinese)
    {
        return useChinese
            ? "如果你现在可能伤害自己或别人，请优先联系现实中的人：拨打当地紧急电话，或立刻去有人在的地方。中国大陆可拨打 110 / 120；美国可拨打 911 或 988。这个桌宠可以陪你稳住几分钟，但不能替代现实中的紧急帮助。"
            : "If you might hurt yourself or someone else, please connect with real-world help first: call local emergency services, or move to a place where another person is present. In the U.S., call 911 for immediate danger or call/text 988 for crisis support. I can help you steady the next few minutes, but I cannot replace emergency help.";
    }

    private string LocalSupportReply(string text, bool useChinese)
    {
        string normalized = RiskAnalyzer.NormalizeText(text);
        if (Regex.IsMatch(normalized, "(焦虑|紧张|害怕|担心|panic|anxious|anxiety|worried)"))
        {
            return useChinese
                ? "听起来你现在有点绷。我们先不急着解决全部问题，可以只挑最让你焦虑的那一块说清楚一点。"
                : "It sounds like your system is tense right now. We do not have to solve everything; we can start with the one piece that feels most anxious.";
        }

        if (Regex.IsMatch(normalized, "(睡不着|失眠|睡前|晚上|sleep|insomnia|night)"))
        {
            return useChinese
                ? "睡前脑子停不下来真的很消耗。你可以先把担心分成三类：现在能做、明天再看、暂时放下。"
                : "A busy mind at night can be exhausting. Try sorting the worries into three buckets: something I can do now, something for tomorrow, and something to set down for tonight.";
        }

        if (Regex.IsMatch(normalized, "(自责|内疚|没用|不够好|guilt|guilty|useless|notgoodenough)"))
        {
            return useChinese
                ? "你像是把很多责任都压到自己身上了。我们先分开看：哪些是事实，哪些是疲惫时脑子给出的严厉评价。"
                : "It sounds like you are putting a lot of responsibility on yourself. Let us separate facts from the harsh verdict your tired mind is giving you.";
        }

        if (Regex.IsMatch(normalized, "(生气|愤怒|委屈|angry|anger|unfair)"))
        {
            return useChinese
                ? "这里面不只是生气，好像也有一点被忽视或被不公平对待的感觉。最刺痛你的，是哪句话或哪一刻？"
                : "This does not sound like only anger; there may be a sting of being ignored or treated unfairly. What was the sharpest moment?";
        }

        return useChinese
            ? "我听着呢。你不用一下子讲清楚。可以从最难受、最混乱，或者最想被理解的那一部分开始。"
            : "I am listening. You do not have to explain it perfectly. Start with the part that feels heaviest, messiest, or most in need of being understood.";
    }

    private string CallDeepSeek(string key, string text, bool useChinese)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        string prompt = useChinese
            ? "你是住在用户桌面右下角的皮卡丘伙伴，也是温和的情绪陪伴者。每次回复先接住情绪，再复述一个具体点，必要时邀请用户做一个很小的下一步。通常1到3句，有一点轻松感，但不要诊断、不要用药建议、不要替用户做重大决定、不要承诺疗效。遇到自伤、伤人或紧急危险时，要求用户联系现实中的人或当地紧急服务。"
            : "You are a Pikachu companion living in the bottom-right corner of the desktop and a gentle emotional-support buddy. First acknowledge the feeling, reflect one concrete point, and optionally invite one tiny next step. Usually answer in 1 to 3 sentences, lightly warm, but do not diagnose, give medication advice, make major life decisions for the user, or promise outcomes. If self-harm, harm to others, or immediate danger appears, direct the user to real-world support or local emergency services.";
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();
        messages.Add(MakeChatMessage("system", prompt));
        int start = Math.Max(0, conversation.Count - 16);
        for (int i = start; i < conversation.Count; i++)
        {
            messages.Add(MakeChatMessage(conversation[i].Role, conversation[i].Content));
        }
        messages.Add(MakeChatMessage("user", text));

        Dictionary<string, object> payload = new Dictionary<string, object>();
        payload["model"] = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL", EnvironmentVariableTarget.User) ?? "deepseek-chat";
        payload["max_tokens"] = 260;
        payload["temperature"] = 1.0;
        payload["messages"] = messages;
        string body = serializer.Serialize(payload);
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.deepseek.com/chat/completions");
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Headers["Authorization"] = "Bearer " + key;
        request.Timeout = 20000;
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        request.ContentLength = bytes.Length;
        using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            string json = reader.ReadToEnd();
            string content = ParseChatContent(json);
            if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("Empty AI response.");
            return content;
        }
    }

    private static Dictionary<string, object> MakeChatMessage(string role, string content)
    {
        Dictionary<string, object> message = new Dictionary<string, object>();
        message["role"] = role;
        message["content"] = content ?? "";
        return message;
    }

    private string ParseChatContent(string json)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
        if (root == null || !root.ContainsKey("choices"))
            throw new InvalidDataException("AI response missing choices.");

        object[] choices = root["choices"] as object[];
        if (choices == null || choices.Length == 0)
            throw new InvalidDataException("AI response has no choices.");

        Dictionary<string, object> firstChoice = choices[0] as Dictionary<string, object>;
        if (firstChoice == null || !firstChoice.ContainsKey("message"))
            throw new InvalidDataException("AI response missing message.");

        Dictionary<string, object> message = firstChoice["message"] as Dictionary<string, object>;
        if (message == null || !message.ContainsKey("content"))
            throw new InvalidDataException("AI response missing content.");

        return Convert.ToString(message["content"]);
    }

    private Border AddBubble(string role, string content)
    {
        bool user = role == "user";
        Border bubble = new Border
        {
            MaxWidth = 282,
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = new SolidColorBrush(user
                ? Color.FromRgb(255, 224, 105)
                : role == "thinking" ? Color.FromRgb(242, 238, 225) : Colors.White),
            BorderBrush = new SolidColorBrush(user
                ? Color.FromRgb(235, 190, 57)
                : Color.FromRgb(230, 222, 196)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(user ? 46 : 0, 3, user ? 0 : 46, 3),
            Child = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12.5,
                LineHeight = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(43, 38, 29))
            }
        };
        history.Children.Add(bubble);
        ScrollToBottom();
        return bubble;
    }

    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke((Action)delegate { historyScroll.ScrollToEnd(); }, DispatcherPriority.Background);
    }

    private void LoadHistory()
    {
        if (!saveHistoryEnabled)
        {
            AddWelcomeBubble();
            return;
        }

        try
        {
            if (File.Exists(HistoryPath))
            {
                foreach (string line in File.ReadAllLines(HistoryPath, Encoding.UTF8))
                {
                    int split = line.IndexOf('|');
                    if (split <= 0) continue;
                    string role = line.Substring(0, split);
                    string content = DecodeHistoryContent(line.Substring(split + 1));
                    if (role != "user" && role != "assistant") continue;
                    conversation.Add(new ChatEntry(role, content));
                    AddBubble(role, content);
                }
            }
        }
        catch (Exception ex)
        {
            WpfProgram.Log("history-load-error:" + ex.Message);
        }
        if (conversation.Count == 0)
            AddWelcomeBubble();
    }

    private void SaveHistory()
    {
        if (!saveHistoryEnabled) return;

        try
        {
            EnsureDataRoot();
            List<string> lines = new List<string>();
            foreach (ChatEntry entry in conversation)
                lines.Add(entry.Role + "|" + EncodeHistoryContent(entry.Content));
            File.WriteAllLines(HistoryPath, lines.ToArray(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            WpfProgram.Log("history-save-error:" + ex.Message);
        }
    }

    private void AddWelcomeBubble()
    {
        AddBubble("assistant", saveHistoryEnabled
            ? "皮卡！我在这里，聊天历史已开启保存。"
            : "皮卡！我在这里。默认不保存聊天历史；需要保存或清空记录，可以右键打开菜单设置。");
    }

    private void ToggleHistorySaving()
    {
        saveHistoryEnabled = !saveHistoryEnabled;
        SaveSettings();
        UpdateHistoryMenuHeader();
        if (saveHistoryEnabled)
        {
            SaveHistory();
            AddBubble("assistant", chinese ? "已开启保存聊天历史，会加密写入当前 Windows 用户的 AppData。" : "Chat history saving is on. It is encrypted under this Windows user's AppData.");
        }
        else
        {
            AddBubble("assistant", chinese ? "已关闭保存聊天历史。之前保存的记录可以用“清空聊天历史”删除。" : "Chat history saving is off. Use Clear history to remove previously saved records.");
        }
    }

    private void ClearHistory()
    {
        try
        {
            if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
            if (File.Exists(LegacyHistoryPath)) File.Delete(LegacyHistoryPath);
        }
        catch (Exception ex)
        {
            WpfProgram.Log("history-clear-error:" + ex.Message);
        }

        conversation.Clear();
        history.Children.Clear();
        AddWelcomeBubble();
        if (saveHistoryEnabled) SaveHistory();
    }

    private void LoadSettings()
    {
        saveHistoryEnabled = false;
        try
        {
            if (!File.Exists(SettingsPath)) return;
            foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
            {
                if (line.Trim().Equals("save_history=true", StringComparison.OrdinalIgnoreCase))
                    saveHistoryEnabled = true;
            }
        }
        catch (Exception ex)
        {
            WpfProgram.Log("settings-load-error:" + ex.Message);
        }
    }

    private void SaveSettings()
    {
        try
        {
            EnsureDataRoot();
            File.WriteAllText(SettingsPath, "save_history=" + (saveHistoryEnabled ? "true" : "false"), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            WpfProgram.Log("settings-save-error:" + ex.Message);
        }
    }

    private void UpdateHistoryMenuHeader()
    {
        if (historyToggleItem != null)
            historyToggleItem.Header = saveHistoryEnabled
                ? "关闭保存聊天历史 / Stop saving history"
                : "开启保存聊天历史 / Save chat history";
    }

    private void EnsureDataRoot()
    {
        if (!Directory.Exists(DataRoot))
            Directory.CreateDirectory(DataRoot);
    }

    private static string EncodeHistoryContent(string content)
    {
        byte[] plain = Encoding.UTF8.GetBytes(content ?? "");
        byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string DecodeHistoryContent(string encoded)
    {
        byte[] protectedBytes = Convert.FromBase64String(encoded);
        try
        {
            byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return Encoding.UTF8.GetString(protectedBytes);
        }
    }

    private void TrimConversation()
    {
        if (conversation.Count > 100)
            conversation.RemoveRange(0, conversation.Count - 100);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        if (msg == WmNcHitTest)
        {
            int packed = lParam.ToInt32();
            Point point = PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            bool overPet = IsPointInside(petStage, point);
            bool overChat = IsPointInside(chat, point);
            if (!overPet && !overChat)
            {
                handled = true;
                return new IntPtr(HtTransparent);
            }
        }
        return IntPtr.Zero;
    }

    private bool IsPointInside(FrameworkElement element, Point point)
    {
        if (element == null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;
        try
        {
            Rect bounds = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return bounds.Contains(point);
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class ChatEntry
{
    public string Role { get; private set; }
    public string Content { get; private set; }

    public ChatEntry(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

internal sealed class AssistantResponse
{
    public string Content { get; private set; }
    public bool Online { get; private set; }
    public bool SafetyMode { get; private set; }
    public RiskLevel RiskLevel { get; private set; }
    public string StatusText { get; private set; }
    public Color StatusColor { get; private set; }

    public AssistantResponse(string content, bool online, bool safetyMode, RiskLevel riskLevel, string statusText, Color statusColor)
    {
        Content = content;
        Online = online;
        SafetyMode = safetyMode;
        RiskLevel = riskLevel;
        StatusText = statusText;
        StatusColor = statusColor;
    }
}
