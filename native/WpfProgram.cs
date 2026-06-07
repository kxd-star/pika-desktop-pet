using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        Application app = new Application();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        app.Run(new PikaWindow());
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
    private TextBlock connection;
    private readonly Canvas petStage;
    private Image petImage;
    private Canvas effects;
    private readonly DispatcherTimer idleTimer;
    private readonly Random random = new Random();
    private readonly List<ChatEntry> conversation = new List<ChatEntry>();
    private readonly ScaleTransform petScale = new ScaleTransform(1, 1);
    private readonly RotateTransform petRotate = new RotateTransform(0);
    private readonly TranslateTransform petMove = new TranslateTransform(0, 0);
    private PikaState state = PikaState.Idle;
    private DateTime lastInteraction = DateTime.Now;
    private bool chinese = true;
    private bool sending;
    private bool dragging;
    private Point dragStart;
    private Point windowStart;

    private string HistoryPath
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

        SourceInitialized += delegate
        {
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null) source.AddHook(WindowProc);
        };

        Loaded += delegate
        {
            SetState(PikaState.Idle, false);
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
            Text = "DS 已配置",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(112, 101, 80)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        headerActions.Children.Add(connection);
        headerActions.Children.Add(MakeHeaderButton("中文", delegate { chinese = true; title.Text = "皮卡伙伴"; }));
        headerActions.Children.Add(MakeHeaderButton("EN", delegate { chinese = false; title.Text = "Pika Buddy"; }));
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
            if (!sending) SetState(PikaState.Touch, true);
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
        return stage;
    }

    private ContextMenu BuildContextMenu()
    {
        ContextMenu menu = new ContextMenu();
        MenuItem chatItem = new MenuItem { Header = "打开对话" };
        chatItem.Click += delegate { ToggleChat(true); };
        menu.Items.Add(chatItem);
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
        item.Click += delegate { SetState(target, true); };
        parent.Items.Add(item);
    }

    private void LoadPikachu()
    {
        string local = Path.Combine(WpfProgram.AppRoot, "assets", "pikachu.png");
        if (File.Exists(local))
        {
            SetImageSource(local);
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
                Dispatcher.BeginInvoke((Action)delegate { SetImageSource(local); });
            }
            catch (Exception ex)
            {
                WpfProgram.Log("image-error:" + ex.Message);
            }
        });
    }

    private void SetImageSource(string path)
    {
        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        petImage.Source = bitmap;
    }

    private void ToggleChat(bool visible)
    {
        chat.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        lastInteraction = DateTime.Now;
        if (visible)
        {
            SetState(PikaState.Happy, true);
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
        if (!moved) ToggleChat(chat.Visibility != Visibility.Visible);
        e.Handled = true;
    }

    private void SetState(PikaState next, bool animated)
    {
        state = next;
        if (next != PikaState.Sleepy) lastInteraction = DateTime.Now;
        ClearEffects();
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
            AnimateMoveY(0, -22, 0.34, true);
            AnimateRotate(-4, 4, 0.34, true);
            AddStars();
            ReturnToIdleLater(3.0);
        }
        else if (next == PikaState.Touch)
        {
            AnimateMoveX(-5, 5, 0.11, true);
            AnimateRotate(-3, 3, 0.11, true);
            AddSparks();
            ReturnToIdleLater(2.2);
        }
        else if (next == PikaState.Thinking)
        {
            petRotate.Angle = -8;
            petMove.Y = 5;
            AddThought();
        }
        else if (next == PikaState.Talking)
        {
            AnimateBothScale(1, 1.035, 0.25, true);
            AnimateMoveY(0, -4, 0.25, true);
            AddSpeechDots();
            ReturnToIdleLater(3.0);
        }
        else if (next == PikaState.Sleepy)
        {
            petRotate.Angle = -11;
            petMove.X = 12;
            petMove.Y = 21;
            petScale.ScaleX = petScale.ScaleY = 0.92;
            AnimateBothScale(0.92, 0.94, 2.2, true);
            AddSleep();
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

    private void ReturnToIdleLater(double seconds)
    {
        DispatcherTimer once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        once.Tick += delegate
        {
            once.Stop();
            if (!sending && state != PikaState.Sleepy) SetState(PikaState.Idle, true);
        };
        once.Start();
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
            bool online;
            string answer = Answer(text, chinese, out online);
            Dispatcher.BeginInvoke((Action)delegate
            {
                history.Children.Remove(thinking);
                AddBubble("assistant", answer);
                conversation.Add(new ChatEntry("user", text));
                conversation.Add(new ChatEntry("assistant", answer));
                TrimConversation();
                SaveHistory();
                connection.Text = online ? "DS 在线" : "离线陪伴";
                connection.Foreground = new SolidColorBrush(online
                    ? Color.FromRgb(55, 145, 84)
                    : Color.FromRgb(155, 112, 65));
                sending = false;
                input.IsEnabled = true;
                send.IsEnabled = true;
                SetState(PikaState.Talking, true);
                input.Focus();
            });
        });
    }

    private string Answer(string text, bool useChinese, out bool online)
    {
        try
        {
            string key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(key))
            {
                string answer = CallDeepSeek(key, text, useChinese);
                online = true;
                return answer;
            }
        }
        catch (Exception ex)
        {
            WpfProgram.Log("deepseek-error:" + ex.Message);
        }
        online = false;
        return useChinese
            ? "我听着呢。你想让我陪你一起想办法，还是先听你把话说完？"
            : "I am listening. Do you want ideas, or should I just stay with the thought?";
    }

    private string CallDeepSeek(string key, string text, bool useChinese)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        string prompt = useChinese
            ? "你是住在用户桌面右下角的皮卡丘伙伴。像熟悉用户的朋友一样自然聊天，结合上下文，回复具体、有一点俏皮，通常1到3句，避免客服腔和重复套话。"
            : "You are a Pikachu companion living in the bottom-right corner of the desktop. Chat like a familiar friend, use context, be specific and lightly playful, usually in 1 to 3 sentences, and avoid customer-support language.";
        StringBuilder messages = new StringBuilder();
        messages.Append("{\"role\":\"system\",\"content\":\"").Append(JsonEscape(prompt)).Append("\"}");
        int start = Math.Max(0, conversation.Count - 16);
        for (int i = start; i < conversation.Count; i++)
        {
            messages.Append(",{\"role\":\"").Append(conversation[i].Role)
                .Append("\",\"content\":\"").Append(JsonEscape(conversation[i].Content)).Append("\"}");
        }
        messages.Append(",{\"role\":\"user\",\"content\":\"").Append(JsonEscape(text)).Append("\"}");
        string body = "{\"model\":\"deepseek-chat\",\"max_tokens\":260,\"temperature\":1.0,\"messages\":[" + messages + "]}";
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
            string content = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("Empty DeepSeek response.");
            return Regex.Unescape(content).Replace("\\/", "/");
        }
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
        try
        {
            if (File.Exists(HistoryPath))
            {
                foreach (string line in File.ReadAllLines(HistoryPath, Encoding.UTF8))
                {
                    int split = line.IndexOf('|');
                    if (split <= 0) continue;
                    string role = line.Substring(0, split);
                    string content = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(split + 1)));
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
            AddBubble("assistant", "皮卡！新的渲染层上线了，聊天记录会一直留在这里。");
    }

    private void SaveHistory()
    {
        try
        {
            List<string> lines = new List<string>();
            foreach (ChatEntry entry in conversation)
                lines.Add(entry.Role + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Content)));
            File.WriteAllLines(HistoryPath, lines.ToArray(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            WpfProgram.Log("history-save-error:" + ex.Message);
        }
    }

    private void TrimConversation()
    {
        if (conversation.Count > 100)
            conversation.RemoveRange(0, conversation.Count - 100);
    }

    private static string JsonEscape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        if (msg == WmNcHitTest)
        {
            int packed = lParam.ToInt32();
            Point point = PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
            bool overPet = point.X >= 120 && point.Y >= 415;
            bool overChat = chat.Visibility == Visibility.Visible && point.X >= 8 && point.X <= 378 && point.Y >= 8 && point.Y <= 408;
            if (!overPet && !overChat)
            {
                handled = true;
                return new IntPtr(HtTransparent);
            }
        }
        return IntPtr.Zero;
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
