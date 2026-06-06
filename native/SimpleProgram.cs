using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class SimpleProgram
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new FloatingPikaForm());
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pika-runtime.log"),
                "[" + DateTime.Now.ToString("s") + "] " + message + Environment.NewLine);
        }
        catch
        {
        }
    }
}

internal sealed class FloatingPikaForm : Form
{
    private Panel bubble;
    private Label reply;
    private TextBox input;
    private Button send;
    private readonly Timer timer;
    private readonly ContextMenuStrip menu;
    private Button chineseButton;
    private Button englishButton;
    private Label statusLabel;
    private ToolStripMenuItem showChatItem;
    private ToolStripMenuItem exitItem;

    private bool dragging;
    private bool dragMoved;
    private bool petHovering;
    private bool talking;
    private bool chinese = true;
    private Point dragStart;
    private Point formStart;
    private readonly Random random = new Random();
    private readonly List<int> hoverBag = new List<int>();
    private readonly List<ChatMessage> conversation = new List<ChatMessage>();
    private Image pikaImage;
    private bool loadingImage;
    private float touchBounce;
    private string hoverText = "";
    private DateTime hoverUntil = DateTime.MinValue;
    private double tick;

    private Rectangle PetRect
    {
        get { return new Rectangle(45, 202, 180, 145); }
    }

    public FloatingPikaForm()
    {
        Text = "DesktopPetMVP";
        Width = 270;
        Height = 360;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;

        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(work.Right - Width - 18, work.Bottom - Height - 12);

        bubble = BuildBubble();
        bubble.Visible = false;
        Controls.Add(bubble);

        menu = new ContextMenuStrip();
        showChatItem = new ToolStripMenuItem();
        showChatItem.Click += delegate { ToggleBubble(true); };
        exitItem = new ToolStripMenuItem();
        exitItem.Click += delegate { Close(); };
        menu.Items.Add(showChatItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        ContextMenuStrip = menu;

        timer = new Timer();
        timer.Interval = 40;
        timer.Tick += delegate
        {
            tick += 0.04;
            touchBounce *= 0.84f;
            Invalidate();
        };
        timer.Start();

        ApplyLanguage();
        LoadPikaImageAsync();
        SimpleProgram.Log("floating-ready");
    }

    private void LoadPikaImageAsync()
    {
        if (loadingImage) return;
        loadingImage = true;
        Task.Factory.StartNew(delegate
        {
            Image loaded = null;
            try
            {
                string assetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "pikachu.png");
                if (File.Exists(assetPath))
                {
                    using (Image image = Image.FromFile(assetPath))
                        loaded = new Bitmap(image);
                }
                else
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/25.png");
                    request.UserAgent = "DesktopPetMVP";
                    request.Timeout = 7000;
                    using (WebResponse response = request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (Image image = Image.FromStream(stream))
                        loaded = new Bitmap(image);
                }
            }
            catch (Exception ex)
            {
                SimpleProgram.Log("image-load-error:" + ex.Message);
            }

            if (!IsDisposed)
            {
                BeginInvoke((Action)delegate
                {
                    pikaImage = loaded;
                    loadingImage = false;
                    Invalidate();
                });
            }
        });
    }

    private Panel BuildBubble()
    {
        RoundedPanel panel = new RoundedPanel();
        panel.Location = new Point(4, 4);
        panel.Size = new Size(262, 172);
        panel.BackColor = Color.FromArgb(255, 255, 252, 241);
        panel.BorderColor = Color.FromArgb(232, 207, 135);
        panel.Radius = 14;

        Label title = new Label();
        title.Name = "TitleLabel";
        title.Location = new Point(12, 9);
        title.Size = new Size(86, 22);
        title.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(42, 38, 30);
        panel.Controls.Add(title);

        statusLabel = new Label();
        statusLabel.Location = new Point(91, 10);
        statusLabel.Size = new Size(65, 18);
        statusLabel.Font = new Font("Microsoft YaHei UI", 7.5f);
        statusLabel.ForeColor = Color.FromArgb(112, 103, 85);
        statusLabel.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(statusLabel);

        chineseButton = BuildLanguageButton("中文", new Point(158, 7), 44);
        chineseButton.Click += delegate { SetLanguage(true); };
        panel.Controls.Add(chineseButton);

        englishButton = BuildLanguageButton("EN", new Point(204, 7), 34);
        englishButton.Click += delegate { SetLanguage(false); };
        panel.Controls.Add(englishButton);

        Button close = new Button();
        close.Text = "×";
        close.Location = new Point(238, 6);
        close.Size = new Size(20, 25);
        close.FlatStyle = FlatStyle.Flat;
        close.FlatAppearance.BorderSize = 0;
        close.BackColor = Color.FromArgb(255, 255, 252, 241);
        close.ForeColor = Color.FromArgb(96, 88, 73);
        close.Font = new Font("Segoe UI", 11f);
        close.Click += delegate { ToggleBubble(false); };
        panel.Controls.Add(close);

        reply = new Label();
        reply.Location = new Point(12, 38);
        reply.Size = new Size(238, 79);
        reply.Font = new Font("Microsoft YaHei UI", 9f);
        reply.ForeColor = Color.FromArgb(35, 31, 25);
        reply.BackColor = Color.Transparent;
        reply.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(reply);

        input = new TextBox();
        input.Location = new Point(12, 130);
        input.Size = new Size(192, 26);
        input.Font = new Font("Microsoft YaHei UI", 9f);
        input.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SendChat();
                e.SuppressKeyPress = true;
            }
        };
        panel.Controls.Add(input);

        send = new Button();
        send.Location = new Point(211, 128);
        send.Size = new Size(39, 30);
        send.BackColor = Color.FromArgb(30, 31, 34);
        send.ForeColor = Color.White;
        send.FlatStyle = FlatStyle.Flat;
        send.FlatAppearance.BorderSize = 0;
        send.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        send.Click += delegate { SendChat(); };
        panel.Controls.Add(send);

        return panel;
    }

    private Button BuildLanguageButton(string text, Point location, int width)
    {
        Button button = new Button();
        button.Text = text;
        button.Location = location;
        button.Size = new Size(width, 25);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        return button;
    }

    private void SetLanguage(bool useChinese)
    {
        chinese = useChinese;
        hoverBag.Clear();
        hoverUntil = DateTime.MinValue;
        ApplyLanguage();
        Invalidate();
    }

    private void ApplyLanguage()
    {
        Label title = bubble.Controls["TitleLabel"] as Label;
        if (title != null)
            title.Text = chinese ? "\u76ae\u5361\u4f19\u4f34" : "Pika Buddy";

        reply.Text = chinese
            ? "\u76ae\u5361\uff01\u70b9\u6211\u5c31\u53ef\u4ee5\u804a\u5929\u3002"
            : "Pika! Click me whenever you want to chat.";
        send.Text = chinese ? "\u53d1" : ">";
        showChatItem.Text = chinese ? "\u6253\u5f00\u5bf9\u8bdd" : "Open chat";
        exitItem.Text = chinese ? "\u9000\u51fa\u684c\u5ba0" : "Exit";
        UpdateConnectionStatus(null);

        chineseButton.BackColor = chinese ? Color.FromArgb(255, 216, 77) : Color.FromArgb(245, 241, 226);
        englishButton.BackColor = chinese ? Color.FromArgb(245, 241, 226) : Color.FromArgb(255, 216, 77);
        chineseButton.ForeColor = Color.FromArgb(42, 38, 30);
        englishButton.ForeColor = Color.FromArgb(42, 38, 30);
    }

    private void ToggleBubble(bool open)
    {
        bubble.Visible = open;
        if (open) input.Focus();
        Invalidate();
    }

    private void SendChat()
    {
        string text = input.Text.Trim();
        if (text.Length == 0) return;

        bool responseInChinese = chinese;
        input.Text = "";
        reply.Text = responseInChinese
            ? "\u4f60\uff1a" + text + Environment.NewLine + "\u76ae\u5361\u6b63\u5728\u60f3\u2026\u2026"
            : "You: " + text + Environment.NewLine + "Pika is thinking...";
        send.Enabled = false;
        input.Enabled = false;
        talking = true;
        Invalidate();

        Task.Factory.StartNew(delegate
        {
            string answer = Answer(text, responseInChinese);
            BeginInvoke((Action)delegate
            {
                reply.Text = responseInChinese
                    ? "\u4f60\uff1a" + text + Environment.NewLine + "\u76ae\u5361\uff1a" + answer
                    : "You: " + text + Environment.NewLine + "Pika: " + answer;
                send.Enabled = true;
                input.Enabled = true;
                talking = false;
                UpdateConnectionStatus(lastReplyOnline);
                input.Focus();
                Invalidate();
            });
        });
    }

    private bool lastReplyOnline;

    private string Answer(string text, bool responseInChinese)
    {
        try
        {
            string key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
                key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.User);

            if (!string.IsNullOrWhiteSpace(key))
            {
                string answer = DeepSeek(key, text, responseInChinese);
                lastReplyOnline = true;
                SimpleProgram.Log("deepseek-ok");
                return answer;
            }

            lastReplyOnline = false;
            return LocalReply(text, responseInChinese);
        }
        catch (Exception ex)
        {
            lastReplyOnline = false;
            SimpleProgram.Log("deepseek-error:" + ex.GetType().Name + ":" + ex.Message);
            return LocalReply(text, responseInChinese);
        }
    }

    private string LocalReply(string text, bool responseInChinese)
    {
        string lower = text.ToLowerInvariant();
        if (responseInChinese)
        {
            if (text.Contains("\u7d2f") || text.Contains("\u56f0") || text.Contains("\u70e6"))
                return Pick(new string[] {
                    "\u90a3\u5c31\u5148\u522b\u903c\u81ea\u5df1\u51b2\u523a\u4e86\u3002\u4f60\u60f3\u5b89\u9759\u4e00\u4f1a\uff0c\u8fd8\u662f\u8ddf\u6211\u5410\u69fd\u4e24\u53e5\uff1f",
                    "\u542c\u8d77\u6765\u4eca\u5929\u5df2\u7ecf\u6d88\u8017\u4e0d\u5c11\u4e86\u3002\u5148\u505a\u4e00\u4ef6\u6700\u5c0f\u7684\u4e8b\uff0c\u5269\u4e0b\u7684\u665a\u70b9\u518d\u8bf4\u3002",
                    "\u6765\uff0c\u5148\u628a\u80a9\u8180\u653e\u4e0b\u6765\u3002\u6211\u4e0d\u50ac\u4f60\uff0c\u4f60\u6162\u6162\u8bf4\u3002"
                });
            if (text.Contains("\u4f60\u597d") || lower.Contains("hello") || lower.Contains("hi"))
                return Pick(new string[] {
                    "\u55e8\uff0c\u4f60\u7ec8\u4e8e\u6765\u627e\u6211\u4e86\u3002\u4eca\u5929\u8fc7\u5f97\u600e\u4e48\u6837\uff1f",
                    "\u6211\u5728\u5462\u3002\u4eca\u5929\u60f3\u968f\u4fbf\u804a\u804a\uff0c\u8fd8\u662f\u6709\u4ef6\u4e8b\u60f3\u4e00\u8d77\u7406\u6e05\uff1f",
                    "\u55e8\uff0c\u53f3\u4e0b\u89d2\u5c0f\u7535\u53f0\u5df2\u4e0a\u7ebf\u3002\u4f60\u5148\u8bf4\u3002"
                });
            return Pick(new string[] {
                "\u6211\u542c\u7740\u5462\u3002\u8fd9\u4ef6\u4e8b\u91cc\uff0c\u4f60\u73b0\u5728\u6700\u5728\u610f\u7684\u662f\u54ea\u4e00\u90e8\u5206\uff1f",
                "\u55ef\uff0c\u6211\u5927\u6982\u63a5\u4f4f\u4e86\u3002\u4f60\u8981\u6211\u966a\u4f60\u60f3\u529e\u6cd5\uff0c\u8fd8\u662f\u5148\u542c\u4f60\u8bf4\u5b8c\uff1f",
                "\u8fd9\u53e5\u8bdd\u542c\u8d77\u6765\u80cc\u540e\u8fd8\u6709\u4e00\u70b9\u6545\u4e8b\u3002\u7ee7\u7eed\u8bf4\uff0c\u6211\u5728\u3002",
                "\u6536\u5230\u3002\u6211\u5148\u4e0d\u6025\u7740\u4e0b\u7ed3\u8bba\uff0c\u4f60\u518d\u591a\u544a\u8bc9\u6211\u4e00\u70b9\u3002",
                "\u6211\u8bb0\u4e0b\u4e86\u3002\u5982\u679c\u53ea\u80fd\u5148\u6539\u53d8\u4e00\u4ef6\u5c0f\u4e8b\uff0c\u4f60\u4f1a\u9009\u4ec0\u4e48\uff1f"
            });
        }

        if (lower.Contains("tired") || lower.Contains("stress"))
            return Pick(new string[] {
                "That sounds draining. Want quiet company, or do you want to unpack it together?",
                "Let us lower the bar for the next ten minutes. What is the smallest useful thing you could do?",
                "You do not have to solve all of it right now. Tell me which part feels heaviest."
            });
        if (lower.Contains("hello") || lower.Contains("hi"))
            return Pick(new string[] {
                "Hey, you found me. How is today treating you?",
                "I am here. Are we chatting casually, or untangling something?",
                "Hello from the bottom-right corner. What is on your mind?"
            });
        return Pick(new string[] {
            "I am listening. Which part matters most to you right now?",
            "I think I follow. Do you want ideas, or do you want me to just stay with the thought?",
            "There is probably more behind that sentence. Keep going.",
            "Got it. I will not rush to a conclusion. Tell me a little more.",
            "If you could change one small part of this first, what would it be?"
        });
    }

    private string Pick(string[] choices)
    {
        return choices[random.Next(choices.Length)];
    }

    private string DeepSeek(string key, string text, bool responseInChinese)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        string systemPrompt = responseInChinese
            ? "\u4f60\u662f\u4f4f\u5728\u7528\u6237\u684c\u9762\u53f3\u4e0b\u89d2\u7684\u5c0f\u7535\u6c14\u4f19\u4f34\u3002\u4f60\u50cf\u719f\u6089\u7528\u6237\u7684\u670b\u53cb\uff0c\u4e0d\u662f\u5ba2\u670d\u6216\u5de5\u5177\u52a9\u624b\u3002\u7ed3\u5408\u4e0a\u4e0b\u6587\uff0c\u7528\u81ea\u7136\u3001\u5177\u4f53\u3001\u6709\u4e00\u70b9\u4fcf\u76ae\u7684\u4e2d\u6587\u56de\u590d\u3002\u901a\u5e381\u52303\u53e5\uff0c\u907f\u514d\u5957\u8bdd\u548c\u91cd\u590d\u9f13\u52b1\uff0c\u4e0d\u8981\u6bcf\u6b21\u90fd\u8bf4\u76ae\u5361\u3002\u53ef\u4ee5\u81ea\u7136\u5730\u95ee\u4e00\u4e2a\u8ffd\u95ee\uff0c\u4f46\u4e0d\u8981\u8fde\u73af\u63d0\u95ee\u3002"
            : "You are a tiny electric companion living in the bottom-right corner of the user's desktop. Speak like a familiar friend, never like customer support or a generic assistant. Use the conversation context. Reply naturally and specifically in 1 to 3 sentences, with a little playful personality. Avoid canned encouragement, repetition, and saying pika every time. Ask at most one natural follow-up question.";
        StringBuilder messages = new StringBuilder();
        messages.Append("{\"role\":\"system\",\"content\":\"").Append(JsonEscape(systemPrompt)).Append("\"}");
        int start = Math.Max(0, conversation.Count - 10);
        for (int i = start; i < conversation.Count; i++)
        {
            messages.Append(",{\"role\":\"").Append(conversation[i].Role)
                .Append("\",\"content\":\"").Append(JsonEscape(conversation[i].Content)).Append("\"}");
        }
        messages.Append(",{\"role\":\"user\",\"content\":\"").Append(JsonEscape(text)).Append("\"}");
        string model = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL", EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(model)) model = "deepseek-chat";
        string body = "{\"model\":\"" + JsonEscape(model) +
            "\",\"max_tokens\":220,\"temperature\":1.0,\"messages\":[" + messages + "]}";

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.deepseek.com/chat/completions");
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Accept = "application/json";
        request.UserAgent = "DesktopPetMVP";
        request.Headers["Authorization"] = "Bearer " + key;
        request.Timeout = 15000;
        request.ReadWriteTimeout = 15000;

        byte[] bytes = Encoding.UTF8.GetBytes(body);
        request.ContentLength = bytes.Length;
        using (Stream stream = request.GetRequestStream())
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            string json = reader.ReadToEnd();
            string content = Regex.Match(json, "\"content\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(content))
                return responseInChinese
                    ? "\u6211\u521a\u624d\u60f3\u597d\u4e86\uff0c\u53ef\u662f\u8bdd\u6ca1\u663e\u793a\u51fa\u6765\u3002"
                    : "I thought of an answer, but it did not render.";
            string answer = Regex.Unescape(content).Replace("\\/", "/");
            conversation.Add(new ChatMessage("user", text));
            conversation.Add(new ChatMessage("assistant", answer));
            if (conversation.Count > 14)
                conversation.RemoveRange(0, conversation.Count - 14);
            return answer;
        }
    }

    private void UpdateConnectionStatus(bool? online)
    {
        bool configured = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.User));
        if (online == true)
        {
            statusLabel.Text = chinese ? "DS \u5728\u7ebf" : "DS online";
            statusLabel.ForeColor = Color.FromArgb(55, 143, 82);
        }
        else if (online == false || !configured)
        {
            statusLabel.Text = chinese ? "\u79bb\u7ebf\u966a\u4f34" : "Offline";
            statusLabel.ForeColor = Color.FromArgb(155, 112, 65);
        }
        else
        {
            statusLabel.Text = chinese ? "DS \u5df2\u914d\u7f6e" : "DS ready";
            statusLabel.ForeColor = Color.FromArgb(112, 103, 85);
        }
    }

    private static string JsonEscape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && PetRect.Contains(e.Location))
        {
            dragging = true;
            dragMoved = false;
            dragStart = Cursor.Position;
            formStart = Location;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool overPet = PetRect.Contains(e.Location);
        if (overPet && !petHovering && !bubble.Visible && !dragging)
        {
            petHovering = true;
            ShowHoverLine();
        }
        else if (!overPet)
        {
            petHovering = false;
        }

        if (!dragging) return;

        Point cursor = Cursor.Position;
        int dx = cursor.X - dragStart.X;
        int dy = cursor.Y - dragStart.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 4) dragMoved = true;
        Location = new Point(formStart.X + dx, formStart.Y + dy);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;

        dragging = false;
        Capture = false;
        if (!dragMoved)
            ToggleBubble(!bubble.Visible);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        petHovering = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawPika(g, PetRect);
        if (bubble.Visible)
            DrawBubblePointer(g);
        else if (DateTime.Now < hoverUntil)
            DrawHoverTip(g);
    }

    private void ShowHoverLine()
    {
        string[] lines = chinese ? ChineseHoverLines : EnglishHoverLines;
        if (hoverBag.Count == 0)
        {
            for (int i = 0; i < lines.Length; i++)
                hoverBag.Add(i);
            for (int i = hoverBag.Count - 1; i > 0; i--)
            {
                int swap = random.Next(i + 1);
                int value = hoverBag[i];
                hoverBag[i] = hoverBag[swap];
                hoverBag[swap] = value;
            }
        }

        int index = hoverBag[hoverBag.Count - 1];
        hoverBag.RemoveAt(hoverBag.Count - 1);
        hoverText = lines[index];
        hoverUntil = DateTime.Now.AddSeconds(3);
        touchBounce = 1f;
        Invalidate();
    }

    private static readonly string[] ChineseHoverLines =
    {
        "\u4eca\u5929\u5929\u6c14\u4e0d\u9519\u5440\u3002",
        "\u76ae\u5361\uff01\u4eca\u5929\u4e5f\u8f9b\u82e6\u5566\u3002",
        "\u6478\u6478\u5934\uff1f\u6211\u5728\u8fd9\u513f\u3002",
        "\u4f60\u6765\u5566\uff0c\u6211\u6b63\u597d\u60f3\u8ddf\u4f60\u804a\u804a\u3002",
        "\u5148\u559d\u53e3\u6c34\uff0c\u518d\u7ee7\u7eed\u51b2\u3002",
        "\u521a\u521a\u90a3\u4e00\u4e0b\uff0c\u662f\u4e0d\u662f\u60f3\u6211\u4e86\uff1f",
        "\u4eca\u5929\u4e5f\u8981\u7ed9\u81ea\u5df1\u4e00\u70b9\u5c0f\u5956\u52b1\u3002",
        "\u4f60\u5fd9\u4f60\u7684\uff0c\u6211\u4f1a\u4e56\u4e56\u5f85\u7740\u3002",
        "\u5750\u4e45\u4e86\uff0c\u80a9\u8180\u677e\u4e00\u677e\u5427\u3002",
        "\u76ae\u5361\u76ae\u5361\uff0c\u7535\u91cf\u6ee1\u683c\uff01",
        "\u4e0d\u7740\u6025\uff0c\u6162\u6162\u505a\u4e5f\u662f\u5728\u524d\u8fdb\u3002",
        "\u4f60\u4eca\u5929\u770b\u8d77\u6765\u5f88\u6709\u5e72\u52b2\u3002",
        "\u6211\u53ef\u4ee5\u966a\u4f60\u5b89\u9759\u4e00\u4f1a\u513f\u3002",
        "\u53c8\u89c1\u9762\u5566\uff0c\u8fd9\u6b21\u60f3\u804a\u4ec0\u4e48\uff1f",
        "\u7d2f\u4e86\u5c31\u770b\u6211\u4e00\u773c\uff0c\u5145\u70b9\u7535\u3002",
        "\u8fd9\u4ef6\u4e8b\u4f60\u80af\u5b9a\u80fd\u641e\u5b9a\u3002",
        "\u4eca\u5929\u6709\u6ca1\u6709\u8bb0\u5f97\u5403\u70b9\u597d\u7684\uff1f",
        "\u6211\u521a\u5b66\u4f1a\u4e86\u4e00\u4e2a\u65b0\u8868\u60c5\uff0c\u60f3\u770b\u5417\uff1f",
        "\u5148\u4f38\u4e2a\u61d2\u8170\uff0c\u518d\u7ee7\u7eed\u3002",
        "\u53f3\u4e0b\u89d2\u4e00\u5207\u6b63\u5e38\uff0c\u6211\u4f1a\u5b88\u7740\u4f60\u3002"
    };

    private static readonly string[] EnglishHoverLines =
    {
        "The weather feels nice today.",
        "Pika! You are doing great.",
        "A little head pat?",
        "You are here. I was hoping we could chat.",
        "Take a sip of water before the next push.",
        "Did you just come over because you missed me?",
        "Give yourself a tiny reward today.",
        "You work. I will stay right here.",
        "Long sit? Roll those shoulders.",
        "Pika pika, battery fully charged!",
        "No rush. Slow progress still counts.",
        "You look focused today.",
        "I can keep you quiet company.",
        "Hello again. What is on your mind?",
        "Look at me for a quick energy refill.",
        "You can handle this one.",
        "Remember to eat something good today.",
        "I learned a new expression. Want to see?",
        "Stretch first, then continue.",
        "Bottom-right corner is secure. I am on watch."
    };

    private void DrawHoverTip(Graphics g)
    {
        using (Font font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
        using (Brush panel = new SolidBrush(Color.FromArgb(255, 255, 250, 232)))
        using (Brush text = new SolidBrush(Color.FromArgb(35, 31, 25)))
        using (Pen border = new Pen(Color.FromArgb(225, 180, 80), 1.5f))
        {
            SizeF size = g.MeasureString(hoverText, font, 236);
            RectangleF rect = new RectangleF(
                Math.Max(12, (Width - size.Width - 28) / 2),
                176,
                size.Width + 28,
                size.Height + 18);
            FillRound(g, panel, rect, 18);
            using (GraphicsPath path = RoundPath(rect, 18))
            {
                g.DrawPath(border, path);
            }
            g.DrawString(hoverText, font, text, rect.Left + 14, rect.Top + 9);

            using (Brush pointer = new SolidBrush(Color.FromArgb(255, 255, 250, 232)))
            {
                PointF[] points =
                {
                    new PointF(rect.Left + rect.Width / 2 - 8, rect.Bottom - 1),
                    new PointF(rect.Left + rect.Width / 2 + 8, rect.Bottom - 1),
                    new PointF(rect.Left + rect.Width / 2, rect.Bottom + 12)
                };
                g.FillPolygon(pointer, points);
            }
        }
    }

    private void DrawBubblePointer(Graphics g)
    {
        using (Brush brush = new SolidBrush(Color.FromArgb(255, 255, 250, 232)))
        {
            Point[] points =
            {
                new Point(156, 174),
                new Point(186, 174),
                new Point(180, 204)
            };
            g.FillPolygon(brush, points);
        }
    }

    private void DrawPika(Graphics g, Rectangle rect)
    {
        GraphicsState state = g.Save();
        float bob = (float)Math.Sin(tick * 2.4) * 3f - touchBounce * 8f;
        float pulse = talking ? (float)(1f + Math.Sin(tick * 12) * 0.025f) : 1f;
        float hoverScale = touchBounce > 0.02f ? 1f + touchBounce * 0.08f : 1f;
        float angle = dragging ? (float)Math.Sin(tick * 22) * 3f : (float)Math.Sin(tick * 1.4) * 0.8f;

        g.TranslateTransform(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f + bob);
        g.RotateTransform(angle);
        g.ScaleTransform(pulse * hoverScale, pulse * hoverScale);

        DrawPetShadow(g);
        DrawElectricSparks(g);

        if (pikaImage != null)
        {
            RectangleF imageRect = new RectangleF(-76, -72, 152, 152);
            g.DrawImage(pikaImage, imageRect);
            g.Restore(state);
            return;
        }

        using (Brush yellow = new SolidBrush(Color.FromArgb(255, 216, 77)))
        using (Brush yellowDark = new SolidBrush(Color.FromArgb(232, 171, 47)))
        using (Brush yellowLight = new SolidBrush(Color.FromArgb(255, 239, 132)))
        using (Brush black = new SolidBrush(Color.FromArgb(30, 28, 24)))
        using (Brush red = new SolidBrush(Color.FromArgb(255, 91, 83)))
        using (Brush white = new SolidBrush(Color.White))
        using (Brush blush = new SolidBrush(Color.FromArgb(90, 255, 145, 120)))
        using (Brush cheekShine = new SolidBrush(Color.FromArgb(105, 255, 255, 255)))
        using (Pen outline = new Pen(Color.FromArgb(88, 67, 36), 4.5f))
        using (Pen detailPen = new Pen(Color.FromArgb(88, 67, 36), 3.2f))
        using (Pen smilePen = new Pen(Color.FromArgb(30, 28, 24), 4f))
        {
            g.ScaleTransform(0.72f, 0.72f);
            DrawTail(g, yellowDark, outline);
            DrawEar(g, -62, -88, -22, yellow, black, outline);
            DrawEar(g, 62, -88, 22, yellow, black, outline);

            g.FillEllipse(yellowDark, -57, 17, 114, 96);
            g.DrawEllipse(outline, -57, 17, 114, 96);
            g.FillEllipse(yellowLight, -36, 43, 72, 52);

            g.FillEllipse(yellow, -88, -68, 176, 140);
            g.DrawEllipse(outline, -88, -68, 176, 140);
            g.FillEllipse(yellowLight, -60, -51, 81, 42);

            g.FillEllipse(yellowDark, -68, 86, 58, 24);
            g.DrawEllipse(outline, -68, 86, 58, 24);
            g.FillEllipse(yellowDark, 10, 86, 58, 24);
            g.DrawEllipse(outline, 10, 86, 58, 24);

            g.DrawArc(detailPen, -62, 43, 48, 42, 210, 88);
            g.DrawArc(detailPen, 14, 43, 48, 42, 242, 88);

            g.FillEllipse(black, -50, -20, 25, 35);
            g.FillEllipse(white, -43, -12, 8, 9);
            g.FillEllipse(black, 25, -20, 25, 35);
            g.FillEllipse(white, 32, -12, 8, 9);

            PointF[] nose =
            {
                new PointF(-5, 15), new PointF(5, 15), new PointF(0, 21)
            };
            g.FillPolygon(black, nose);

            g.FillEllipse(blush, -82, 10, 49, 40);
            g.FillEllipse(blush, 33, 10, 49, 40);
            g.FillEllipse(red, -76, 15, 36, 30);
            g.FillEllipse(red, 40, 15, 36, 30);
            g.FillEllipse(cheekShine, -68, 19, 10, 7);
            g.FillEllipse(cheekShine, 48, 19, 10, 7);

            if (talking)
            {
                float h = 12 + (float)Math.Abs(Math.Sin(tick * 20)) * 8;
                g.FillEllipse(black, -15, 29, 30, h);
                g.FillEllipse(red, -8, 34, 16, Math.Max(4, h - 10));
            }
            else
            {
                g.DrawArc(smilePen, -21, 20, 21, 22, 8, 150);
                g.DrawArc(smilePen, 0, 20, 21, 22, 22, 150);
            }
        }

        g.Restore(state);
    }

    private void DrawPetShadow(Graphics g)
    {
        using (Brush shadow = new SolidBrush(Color.FromArgb(42, 35, 28, 18)))
            g.FillEllipse(shadow, -58, 65, 116, 22);
    }

    private void DrawElectricSparks(Graphics g)
    {
        using (Pen spark = new Pen(Color.FromArgb(225, 255, 202, 44), 2.2f))
        using (Brush glow = new SolidBrush(Color.FromArgb(90, 255, 231, 86)))
        {
            float wave = (float)Math.Sin(tick * 5);
            DrawBolt(g, spark, -88, -20 + wave * 4, 0.75f);
            DrawBolt(g, spark, 84, 4 - wave * 3, 0.62f);
            g.FillEllipse(glow, -101, -39 + wave * 4, 14, 14);
            g.FillEllipse(glow, 87, -9 - wave * 3, 12, 12);
        }
    }

    private static void DrawBolt(Graphics g, Pen pen, float x, float y, float scale)
    {
        PointF[] points =
        {
            new PointF(x, y),
            new PointF(x + 10 * scale, y + 7 * scale),
            new PointF(x + 3 * scale, y + 16 * scale),
            new PointF(x + 17 * scale, y + 23 * scale)
        };
        g.DrawLines(pen, points);
    }

    private static void DrawEar(Graphics g, float x, float y, float angle, Brush yellow, Brush black, Pen outline)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform(angle);
        PointF[] ear =
        {
            new PointF(-17, 70), new PointF(-10, 18), new PointF(0, -44), new PointF(13, 18), new PointF(19, 70)
        };
        g.FillPolygon(yellow, ear);
        g.DrawPolygon(outline, ear);
        PointF[] tip =
        {
            new PointF(-10, -3), new PointF(0, -44), new PointF(10, -3)
        };
        g.FillPolygon(black, tip);
        g.Restore(state);
    }

    private static void DrawTail(Graphics g, Brush brush, Pen outline)
    {
        PointF[] tail =
        {
            new PointF(70, 14), new PointF(100, 14), new PointF(100, -24), new PointF(132, -24),
            new PointF(110, -56), new PointF(168, -24), new PointF(138, -24), new PointF(138, 34),
            new PointF(110, 34), new PointF(110, 66), new PointF(70, 42)
        };
        g.FillPolygon(brush, tail);
        g.DrawPolygon(outline, tail);
    }

    private static void FillRound(Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using (GraphicsPath path = RoundPath(rect, radius))
        {
            g.FillPath(brush, path);
        }
    }

    private static GraphicsPath RoundPath(RectangleF rect, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (pikaImage != null)
            pikaImage.Dispose();
        base.OnFormClosed(e);
    }
}

internal sealed class ChatMessage
{
    public string Role { get; private set; }
    public string Content { get; private set; }

    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; }
    public Color BorderColor { get; set; }

    public RoundedPanel()
    {
        DoubleBuffered = true;
        Radius = 14;
        BorderColor = Color.FromArgb(232, 207, 135);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF rect = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath path = BuildPath(rect, Radius))
        using (Brush background = new SolidBrush(BackColor))
        using (Pen border = new Pen(BorderColor, 1.25f))
        {
            e.Graphics.FillPath(background, path);
            e.Graphics.DrawPath(border, path);
            Region = new Region(path);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    private static GraphicsPath BuildPath(RectangleF rect, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        return path;
    }
}
