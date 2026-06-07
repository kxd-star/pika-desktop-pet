using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
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

internal enum PetMood
{
    Idle,
    Happy,
    Touch,
    Thinking,
    Talking,
    Sleepy
}

internal sealed class FloatingPikaForm : Form
{
    private Panel bubble;
    private ChatFlowPanel historyPanel;
    private TextBox input;
    private Button send;
    private readonly Timer timer;
    private readonly ContextMenuStrip menu;
    private Button chineseButton;
    private Button englishButton;
    private Label statusLabel;
    private ToolStripMenuItem showChatItem;
    private ToolStripMenuItem previewMoodItem;
    private ToolStripMenuItem exitItem;

    private bool dragging;
    private bool dragMoved;
    private bool petHovering;
    private bool talking;
    private bool chinese = true;
    private PetMood mood = PetMood.Idle;
    private DateTime moodUntil = DateTime.MinValue;
    private DateTime lastInteraction = DateTime.Now;
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
        get { return new Rectangle(70, 342, 180, 145); }
    }

    public FloatingPikaForm()
    {
        Text = "DesktopPetMVP";
        Width = 320;
        Height = 500;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Color transparencyColor = Color.FromArgb(1, 1, 1);
        BackColor = transparencyColor;
        TransparencyKey = transparencyColor;
        DoubleBuffered = true;

        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(work.Right - Width - 18, work.Bottom - Height - 12);

        bubble = BuildBubble();
        bubble.Visible = false;
        Controls.Add(bubble);

        menu = new ContextMenuStrip();
        showChatItem = new ToolStripMenuItem();
        showChatItem.Click += delegate { ToggleBubble(true); };
        previewMoodItem = new ToolStripMenuItem();
        AddMoodPreviewItem(previewMoodItem, "\u5f85\u673a / Idle", PetMood.Idle);
        AddMoodPreviewItem(previewMoodItem, "\u5f00\u5fc3 / Happy", PetMood.Happy);
        AddMoodPreviewItem(previewMoodItem, "\u89e6\u78b0 / Touch", PetMood.Touch);
        AddMoodPreviewItem(previewMoodItem, "\u601d\u8003 / Thinking", PetMood.Thinking);
        AddMoodPreviewItem(previewMoodItem, "\u8bf4\u8bdd / Talking", PetMood.Talking);
        AddMoodPreviewItem(previewMoodItem, "\u56f0\u5026 / Sleepy", PetMood.Sleepy);
        exitItem = new ToolStripMenuItem();
        exitItem.Click += delegate { Close(); };
        menu.Items.Add(showChatItem);
        menu.Items.Add(previewMoodItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        ContextMenuStrip = menu;

        timer = new Timer();
        timer.Interval = 40;
        timer.Tick += delegate
        {
            tick += 0.04;
            touchBounce *= 0.84f;
            UpdateMood();
            if (pikaImage != null && ImageAnimator.CanAnimate(pikaImage))
                ImageAnimator.UpdateFrames(pikaImage);
            Invalidate();
        };
        timer.Start();

        ApplyLanguage();
        LoadChatHistory();
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
                string assetsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
                string gifPath = Path.Combine(assetsDirectory, "pikachu.gif");
                string pngPath = Path.Combine(assetsDirectory, "pikachu.png");
                if (File.Exists(gifPath))
                {
                    loaded = Image.FromFile(gifPath);
                }
                else if (File.Exists(pngPath))
                {
                    using (Image image = Image.FromFile(pngPath))
                        loaded = new Bitmap(image);
                }
                else
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    string[] urls =
                    {
                        "https://img.pokemondb.net/sprites/home/normal/pikachu.png",
                        "https://play.pokemonshowdown.com/sprites/ani/pikachu.gif",
                        "https://gh-proxy.com/https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/25.png",
                        "https://raw.gitmirror.com/PokeAPI/sprites/master/sprites/pokemon/other/home/25.png",
                        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/25.png",
                        "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/25.png"
                    };
                    foreach (string url in urls)
                    {
                        try
                        {
                            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                            request.UserAgent = "DesktopPetMVP";
                            request.Timeout = 8000;
                            using (WebResponse response = request.GetResponse())
                            using (Stream stream = response.GetResponseStream())
                            using (MemoryStream memory = new MemoryStream())
                            {
                                stream.CopyTo(memory);
                                byte[] data = memory.ToArray();
                                Directory.CreateDirectory(assetsDirectory);
                                bool gif = data.Length > 3 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46;
                                string target = gif ? gifPath : pngPath;
                                File.WriteAllBytes(target, data);
                                if (gif)
                                    loaded = Image.FromFile(target);
                                else
                                {
                                    using (Image image = Image.FromFile(target))
                                        loaded = new Bitmap(image);
                                }
                            }
                            if (loaded != null) break;
                        }
                        catch (Exception assetError)
                        {
                            SimpleProgram.Log("image-source-error:" + assetError.Message);
                        }
                    }
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
                    if (pikaImage != null && ImageAnimator.CanAnimate(pikaImage))
                        ImageAnimator.Animate(pikaImage, delegate { });
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
        panel.Size = new Size(312, 312);
        panel.BackColor = Color.FromArgb(255, 255, 252, 241);
        panel.BorderColor = Color.FromArgb(232, 207, 135);
        panel.Radius = 14;

        Label title = new Label();
        title.Name = "TitleLabel";
        title.Location = new Point(12, 9);
        title.Size = new Size(92, 22);
        title.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(42, 38, 30);
        panel.Controls.Add(title);

        statusLabel = new Label();
        statusLabel.Location = new Point(100, 10);
        statusLabel.Size = new Size(74, 18);
        statusLabel.Font = new Font("Microsoft YaHei UI", 7.5f);
        statusLabel.ForeColor = Color.FromArgb(112, 103, 85);
        statusLabel.TextAlign = ContentAlignment.MiddleRight;
        panel.Controls.Add(statusLabel);

        chineseButton = BuildLanguageButton("中文", new Point(180, 7), 44);
        chineseButton.Click += delegate { SetLanguage(true); };
        panel.Controls.Add(chineseButton);

        englishButton = BuildLanguageButton("EN", new Point(226, 7), 34);
        englishButton.Click += delegate { SetLanguage(false); };
        panel.Controls.Add(englishButton);

        Button close = new Button();
        close.Text = "×";
        close.Location = new Point(278, 6);
        close.Size = new Size(24, 25);
        close.FlatStyle = FlatStyle.Flat;
        close.FlatAppearance.BorderSize = 0;
        close.BackColor = Color.FromArgb(255, 255, 252, 241);
        close.ForeColor = Color.FromArgb(96, 88, 73);
        close.Font = new Font("Segoe UI", 11f);
        close.Click += delegate { ToggleBubble(false); };
        panel.Controls.Add(close);

        historyPanel = new ChatFlowPanel();
        historyPanel.Location = new Point(10, 38);
        historyPanel.Size = new Size(292, 218);
        historyPanel.FlowDirection = FlowDirection.TopDown;
        historyPanel.WrapContents = false;
        historyPanel.AutoScroll = true;
        historyPanel.Padding = new Padding(0, 4, 0, 4);
        historyPanel.BackColor = Color.FromArgb(255, 255, 252, 241);
        panel.Controls.Add(historyPanel);

        input = new TextBox();
        input.Location = new Point(12, 270);
        input.Size = new Size(238, 26);
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
        send.Location = new Point(258, 268);
        send.Size = new Size(42, 30);
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

    private void AddMoodPreviewItem(ToolStripMenuItem parent, string text, PetMood previewMood)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(text);
        item.Click += delegate
        {
            mood = previewMood;
            moodUntil = DateTime.Now.AddSeconds(6);
            lastInteraction = DateTime.Now;
            Invalidate();
        };
        parent.DropDownItems.Add(item);
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

        send.Text = chinese ? "\u53d1" : ">";
        showChatItem.Text = chinese ? "\u6253\u5f00\u5bf9\u8bdd" : "Open chat";
        previewMoodItem.Text = chinese ? "\u9884\u89c8\u516d\u79cd\u5f62\u6001" : "Preview six poses";
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
        if (open)
        {
            SetMood(PetMood.Happy, 1.8);
            ScrollHistoryToBottom();
            input.Focus();
        }
        Invalidate();
    }

    private void SendChat()
    {
        string text = input.Text.Trim();
        if (text.Length == 0) return;

        lastInteraction = DateTime.Now;
        bool responseInChinese = chinese;
        input.Text = "";
        AddMessageBubble("user", text);
        Label thinkingBubble = AddMessageBubble("thinking",
            responseInChinese ? "\u76ae\u5361\u6b63\u5728\u60f3\u2026\u2026" : "Pika is thinking...");
        send.Enabled = false;
        input.Enabled = false;
        talking = true;
        SetMood(PetMood.Thinking, 30);
        Invalidate();

        Task.Factory.StartNew(delegate
        {
            string answer = Answer(text, responseInChinese);
            BeginInvoke((Action)delegate
            {
                if (thinkingBubble.Parent != null && thinkingBubble.Parent.Parent != null)
                {
                    Control thinkingRow = thinkingBubble.Parent.Parent;
                    historyPanel.Controls.Remove(thinkingRow);
                    thinkingRow.Dispose();
                }
                AddMessageBubble("assistant", answer);
                conversation.Add(new ChatMessage("user", text));
                conversation.Add(new ChatMessage("assistant", answer));
                if (conversation.Count > 80)
                    conversation.RemoveRange(0, conversation.Count - 80);
                SaveChatHistory();
                send.Enabled = true;
                input.Enabled = true;
                talking = false;
                SetMood(PetMood.Talking, 2.4);
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
            return Regex.Unescape(content).Replace("\\/", "/");
        }
    }

    private Label AddMessageBubble(string role, string content)
    {
        bool user = role == "user";
        bool thinking = role == "thinking";
        Font font = new Font("Microsoft YaHei UI", 8.5f);

        Label label = new Label();
        label.Text = content;
        label.Font = font;
        label.ForeColor = Color.FromArgb(39, 35, 28);
        label.BackColor = Color.Transparent;
        label.AutoSize = true;
        label.MaximumSize = new Size(218, 0);
        label.UseCompatibleTextRendering = false;
        Size preferred = label.PreferredSize;
        int bubbleWidth = Math.Min(242, Math.Max(76, preferred.Width + 24));
        int bubbleHeight = Math.Max(36, preferred.Height + 20);

        Panel row = new Panel();
        row.Size = new Size(276, bubbleHeight + 8);
        row.Margin = new Padding(0, 1, 0, 1);
        row.BackColor = Color.Transparent;

        RoundedPanel message = new RoundedPanel();
        message.Size = new Size(bubbleWidth, bubbleHeight);
        message.Location = new Point(user ? row.Width - bubbleWidth - 4 : 4, 2);
        message.Radius = 13;
        message.BackColor = user
            ? Color.FromArgb(255, 224, 105)
            : thinking ? Color.FromArgb(242, 238, 225) : Color.White;
        message.BorderColor = user
            ? Color.FromArgb(235, 190, 57)
            : Color.FromArgb(230, 222, 196);

        label.Location = new Point(11, 8);
        message.Controls.Add(label);
        row.Controls.Add(message);
        historyPanel.Controls.Add(row);
        ScrollHistoryToBottom();
        return label;
    }

    private void ScrollHistoryToBottom()
    {
        if (historyPanel == null || historyPanel.Controls.Count == 0) return;
        historyPanel.PerformLayout();
        historyPanel.ScrollControlIntoView(historyPanel.Controls[historyPanel.Controls.Count - 1]);
        historyPanel.HideNativeScrollBar();
    }

    private string HistoryPath
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat-history.txt"); }
    }

    private void LoadChatHistory()
    {
        historyPanel.Controls.Clear();
        conversation.Clear();
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
                    conversation.Add(new ChatMessage(role, content));
                    AddMessageBubble(role, content);
                }
            }
        }
        catch (Exception ex)
        {
            SimpleProgram.Log("history-load-error:" + ex.Message);
        }

        if (conversation.Count == 0)
            AddMessageBubble("assistant", chinese
                ? "\u76ae\u5361\uff01\u8fd9\u91cc\u4f1a\u4fdd\u7559\u6211\u4eec\u7684\u804a\u5929\u8bb0\u5f55\u3002"
                : "Pika! Our chat history will stay here.");
        ScrollHistoryToBottom();
    }

    private void SaveChatHistory()
    {
        try
        {
            List<string> lines = new List<string>();
            int start = Math.Max(0, conversation.Count - 80);
            for (int i = start; i < conversation.Count; i++)
            {
                lines.Add(conversation[i].Role + "|" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(conversation[i].Content)));
            }
            File.WriteAllLines(HistoryPath, lines.ToArray(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            SimpleProgram.Log("history-save-error:" + ex.Message);
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

    private void SetMood(PetMood next, double seconds)
    {
        mood = next;
        moodUntil = DateTime.Now.AddSeconds(seconds);
        if (next != PetMood.Sleepy)
            lastInteraction = DateTime.Now;
        Invalidate();
    }

    private void UpdateMood()
    {
        if (dragging)
        {
            mood = PetMood.Happy;
            return;
        }
        if (talking && mood != PetMood.Thinking)
        {
            mood = PetMood.Talking;
            return;
        }
        if (DateTime.Now < moodUntil) return;
        mood = DateTime.Now - lastInteraction > TimeSpan.FromSeconds(45)
            ? PetMood.Sleepy
            : PetMood.Idle;
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
            lastInteraction = DateTime.Now;
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
            lastInteraction = DateTime.Now;
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
        {
            SetMood(PetMood.Happy, 1.8);
            ToggleBubble(!bubble.Visible);
        }
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
        SetMood(PetMood.Touch, 2.2);
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
                new Point(176, 314),
                new Point(206, 314),
                new Point(190, 342)
            };
            g.FillPolygon(brush, points);
        }
    }

    private void DrawPika(Graphics g, Rectangle rect)
    {
        GraphicsState state = g.Save();
        float bob = (float)Math.Sin(tick * 2.4) * 2.2f - touchBounce * 8f;
        float pulse = mood == PetMood.Talking ? (float)(1f + Math.Sin(tick * 12) * 0.035f) : 1f;
        float hoverScale = touchBounce > 0.02f ? 1f + touchBounce * 0.08f : 1f;
        float angle = dragging ? (float)Math.Sin(tick * 22) * 3f : (float)Math.Sin(tick * 1.4) * 0.8f;
        float scaleX = pulse * hoverScale;
        float scaleY = scaleX;
        float shiftX = 0;

        if (mood == PetMood.Happy)
        {
            bob -= (float)Math.Abs(Math.Sin(tick * 7)) * 14f;
            angle = (float)Math.Sin(tick * 8) * 4f;
        }
        else if (mood == PetMood.Touch)
        {
            shiftX = (float)Math.Sin(tick * 24) * 4f;
            angle = (float)Math.Sin(tick * 18) * 3f;
        }
        else if (mood == PetMood.Thinking)
        {
            angle = -7f + (float)Math.Sin(tick * 2) * 1.5f;
            bob += 2f;
        }
        else if (mood == PetMood.Sleepy)
        {
            angle = 1.5f;
            bob += 8f + (float)Math.Sin(tick * 1.2) * 1.2f;
        }

        g.TranslateTransform(rect.Left + rect.Width / 2f + shiftX, rect.Top + rect.Height / 2f + bob);
        g.RotateTransform(angle);
        g.ScaleTransform(scaleX, scaleY);

        DrawPetShadow(g);
        if (mood == PetMood.Touch || mood == PetMood.Happy)
            DrawElectricSparks(g);

        if (pikaImage != null && mood == PetMood.Idle)
        {
            RectangleF imageRect = new RectangleF(-76, -72, 152, 152);
            g.DrawImage(pikaImage, imageRect);
            DrawMoodEffects(g);
            g.Restore(state);
            return;
        }

        DrawPosePikachu(g);

        DrawMoodEffects(g);
        g.Restore(state);
    }

    private void DrawPosePikachu(Graphics g)
    {
        using (Brush yellow = new SolidBrush(Color.FromArgb(255, 216, 77)))
        using (Brush yellowDark = new SolidBrush(Color.FromArgb(225, 164, 43)))
        using (Brush yellowLight = new SolidBrush(Color.FromArgb(255, 239, 132)))
        using (Brush black = new SolidBrush(Color.FromArgb(30, 28, 24)))
        using (Brush red = new SolidBrush(Color.FromArgb(255, 82, 76)))
        using (Brush white = new SolidBrush(Color.White))
        using (Brush cheekShine = new SolidBrush(Color.FromArgb(125, 255, 255, 255)))
        using (Pen outline = new Pen(Color.FromArgb(88, 67, 36), 4.5f))
        using (Pen detail = new Pen(Color.FromArgb(88, 67, 36), 3.2f))
        {
            g.ScaleTransform(0.72f, 0.72f);
            if (mood == PetMood.Sleepy)
            {
                DrawSleepyPikachu(g, yellow, yellowDark, yellowLight, black, red, cheekShine, outline, detail);
                return;
            }

            float headX = 0, headY = -3, headW = 176, headH = 140;
            RectangleF body = new RectangleF(-57, 17, 114, 96);
            float leftEarX = -62, rightEarX = 62, earY = -88, leftEarAngle = -22, rightEarAngle = 22;
            RectangleF leftArm = new RectangleF(-72, 39, 39, 56);
            RectangleF rightArm = new RectangleF(33, 39, 39, 56);
            float leftArmAngle = 0, rightArmAngle = 0;
            RectangleF leftFoot = new RectangleF(-68, 86, 58, 24);
            RectangleF rightFoot = new RectangleF(10, 86, 58, 24);
            bool drawFeet = true;

            if (mood == PetMood.Happy)
            {
                body = new RectangleF(-52, 20, 104, 88);
                leftEarAngle = -34; rightEarAngle = 34;
                leftArm = new RectangleF(-78, -4, 34, 68);
                rightArm = new RectangleF(44, -4, 34, 68);
                leftArmAngle = -38; rightArmAngle = 38;
                leftFoot = new RectangleF(-79, 83, 58, 25);
                rightFoot = new RectangleF(21, 83, 58, 25);
            }
            else if (mood == PetMood.Touch)
            {
                headY = 4; headW = 184; headH = 132;
                body = new RectangleF(-66, 25, 132, 80);
                leftEarX = -77; rightEarX = 77; earY = -75;
                leftEarAngle = -58; rightEarAngle = 58;
                leftArm = new RectangleF(-91, 24, 42, 48);
                rightArm = new RectangleF(49, 24, 42, 48);
                leftArmAngle = -76; rightArmAngle = 76;
                leftFoot = new RectangleF(-82, 83, 66, 27);
                rightFoot = new RectangleF(16, 83, 66, 27);
            }
            else if (mood == PetMood.Thinking)
            {
                headX = -8; headY = -12;
                body = new RectangleF(-57, 30, 114, 82);
                leftEarAngle = -18; rightEarAngle = 54;
                leftArm = new RectangleF(-57, 22, 36, 58);
                rightArm = new RectangleF(25, 2, 34, 62);
                leftArmAngle = 18; rightArmAngle = -42;
                leftFoot = new RectangleF(-70, 84, 64, 28);
                rightFoot = new RectangleF(6, 84, 64, 28);
            }
            else if (mood == PetMood.Talking)
            {
                headX = 5; headY = -4;
                leftEarAngle = -28; rightEarAngle = 26;
                leftArm = new RectangleF(-75, 35, 38, 58);
                rightArm = new RectangleF(42, -14, 36, 72);
                leftArmAngle = -15; rightArmAngle = 38 + (float)Math.Sin(tick * 14) * 15f;
            }
            DrawTailPose(g, yellow, outline, mood);
            DrawEar(g, leftEarX, earY, leftEarAngle, yellow, black, outline);
            DrawEar(g, rightEarX, earY, rightEarAngle, yellow, black, outline);

            g.FillEllipse(yellow, body);
            g.DrawEllipse(outline, body);
            g.FillEllipse(yellowLight, body.X + body.Width * 0.23f, body.Y + body.Height * 0.42f,
                body.Width * 0.54f, body.Height * 0.42f);

            if (drawFeet)
            {
                g.FillEllipse(yellow, leftFoot); g.DrawEllipse(outline, leftFoot);
                g.FillEllipse(yellow, rightFoot); g.DrawEllipse(outline, rightFoot);
            }

            DrawLimb(g, yellow, outline, leftArm, leftArmAngle);
            DrawLimb(g, yellow, outline, rightArm, rightArmAngle);

            RectangleF head = new RectangleF(headX - headW / 2f, headY - headH / 2f, headW, headH);
            g.FillEllipse(yellow, head);
            g.DrawEllipse(outline, head);
            g.FillEllipse(yellowLight, head.X + 28, head.Y + 15, 78, 38);

            DrawPikaFace(g, headX, headY, black, red, white, cheekShine, detail);

            g.FillPolygon(yellowDark, new PointF[] {
                new PointF(body.Right - 12, body.Top + 18), new PointF(body.Right + 8, body.Top + 26),
                new PointF(body.Right - 10, body.Top + 35), new PointF(body.Right + 8, body.Top + 44),
                new PointF(body.Right - 10, body.Top + 53)
            });
        }
    }

    private static void DrawSleepyPikachu(
        Graphics g, Brush yellow, Brush yellowDark, Brush yellowLight, Brush black,
        Brush red, Brush cheekShine, Pen outline, Pen detail)
    {
        GraphicsState tailState = g.Save();
        g.TranslateTransform(34, 42);
        g.RotateTransform(24);
        g.ScaleTransform(0.52f, 0.52f);
        DrawTail(g, yellow, outline);
        g.Restore(tailState);

        DrawFoldedEar(g, yellow, black, outline, true);
        DrawFoldedEar(g, yellow, black, outline, false);

        RectangleF body = new RectangleF(-18, 15, 124, 86);
        g.FillEllipse(yellow, body);
        g.DrawEllipse(outline, body);
        g.FillEllipse(yellowLight, 16, 49, 62, 34);
        g.FillPolygon(yellowDark, new PointF[] {
            new PointF(73, 31), new PointF(92, 38), new PointF(76, 47),
            new PointF(94, 55), new PointF(77, 64)
        });

        RectangleF head = new RectangleF(-96, -18, 132, 106);
        g.FillEllipse(yellow, head);
        g.DrawEllipse(outline, head);
        g.FillEllipse(yellowLight, -68, -3, 70, 31);

        g.DrawArc(detail, -70, 20, 28, 20, 12, 158);
        g.DrawArc(detail, -25, 20, 28, 20, 12, 158);
        g.FillEllipse(red, -88, 39, 31, 25);
        g.FillEllipse(red, 1, 39, 31, 25);
        g.FillEllipse(cheekShine, -81, 43, 9, 6);
        g.FillEllipse(cheekShine, 8, 43, 9, 6);
        g.FillPolygon(black, new PointF[] {
            new PointF(-38, 43), new PointF(-29, 43), new PointF(-34, 49)
        });
        g.DrawArc(detail, -46, 48, 25, 18, 20, 140);

        RectangleF leftPaw = new RectangleF(-57, 67, 49, 24);
        RectangleF rightPaw = new RectangleF(-15, 67, 49, 24);
        g.FillEllipse(yellow, leftPaw); g.DrawEllipse(outline, leftPaw);
        g.FillEllipse(yellow, rightPaw); g.DrawEllipse(outline, rightPaw);
    }

    private static void DrawFoldedEar(Graphics g, Brush yellow, Brush black, Pen outline, bool left)
    {
        PointF[] ear = left
            ? new PointF[] {
                new PointF(-87, -24), new PointF(-45, -22), new PointF(-5, -5),
                new PointF(-24, 10), new PointF(-70, -5)
            }
            : new PointF[] {
                new PointF(-5, -10), new PointF(38, -34), new PointF(91, -28),
                new PointF(65, -7), new PointF(20, 7)
            };
        g.FillPolygon(yellow, ear);
        g.DrawPolygon(outline, ear);

        PointF[] tip = left
            ? new PointF[] {
                new PointF(-87, -24), new PointF(-70, -5), new PointF(-53, -11), new PointF(-69, -23)
            }
            : new PointF[] {
                new PointF(91, -28), new PointF(65, -7), new PointF(49, -14), new PointF(67, -29)
            };
        g.FillPolygon(black, tip);
    }

    private void DrawPikaFace(Graphics g, float x, float y, Brush black, Brush red, Brush white, Brush shine, Pen detail)
    {
        float eyeY = y - 17;
        if (mood == PetMood.Sleepy || mood == PetMood.Happy)
        {
            g.DrawArc(detail, x - 51, eyeY, 28, 22, 10, 160);
            g.DrawArc(detail, x + 23, eyeY, 28, 22, 10, 160);
        }
        else
        {
            float eyeW = mood == PetMood.Touch ? 31 : 25;
            float eyeH = mood == PetMood.Touch ? 41 : 35;
            g.FillEllipse(black, x - 50, eyeY - 8, eyeW, eyeH);
            g.FillEllipse(white, x - 43, eyeY - 1, 8, 9);
            g.FillEllipse(black, x + 25, eyeY - 8, eyeW, eyeH);
            g.FillEllipse(white, x + 32, eyeY - 1, 8, 9);
            if (mood == PetMood.Thinking)
            {
                g.FillEllipse(white, x - 42, eyeY - 4, 10, 10);
                g.FillEllipse(white, x + 33, eyeY - 4, 10, 10);
            }
        }

        g.FillEllipse(red, x - 76, y + 12, 36, 30);
        g.FillEllipse(red, x + 40, y + 12, 36, 30);
        g.FillEllipse(shine, x - 68, y + 16, 10, 7);
        g.FillEllipse(shine, x + 48, y + 16, 10, 7);
        g.FillPolygon(black, new PointF[] {
            new PointF(x - 5, y + 13), new PointF(x + 5, y + 13), new PointF(x, y + 19)
        });

        if (mood == PetMood.Touch)
        {
            g.FillEllipse(black, x - 11, y + 27, 22, 27);
            g.FillEllipse(red, x - 5, y + 40, 10, 8);
        }
        else if (mood == PetMood.Talking)
        {
            float h = 20 + (float)Math.Abs(Math.Sin(tick * 20)) * 9;
            g.FillEllipse(black, x - 17, y + 25, 34, h);
            g.FillEllipse(red, x - 9, y + 38, 18, 9);
        }
        else if (mood == PetMood.Sleepy)
        {
            g.DrawArc(detail, x - 13, y + 22, 26, 18, 20, 140);
        }
        else
        {
            g.DrawArc(detail, x - 22, y + 20, 22, 23, 8, 150);
            g.DrawArc(detail, x, y + 20, 22, 23, 22, 150);
        }
    }

    private static void DrawLimb(Graphics g, Brush brush, Pen outline, RectangleF rect, float angle)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(rect.Left + rect.Width / 2f, rect.Top + rect.Height / 2f);
        g.RotateTransform(angle);
        RectangleF local = new RectangleF(-rect.Width / 2f, -rect.Height / 2f, rect.Width, rect.Height);
        g.FillEllipse(brush, local);
        g.DrawEllipse(outline, local);
        g.Restore(state);
    }

    private static void DrawTailPose(Graphics g, Brush brush, Pen outline, PetMood pose)
    {
        GraphicsState state = g.Save();
        if (pose == PetMood.Happy) g.RotateTransform(-15);
        else if (pose == PetMood.Touch) g.RotateTransform(18);
        else if (pose == PetMood.Sleepy)
        {
            g.TranslateTransform(22, 55);
            g.RotateTransform(65);
            g.ScaleTransform(0.75f, 0.75f);
        }
        DrawTail(g, brush, outline);
        g.Restore(state);
    }

    private void DrawMoodEffects(Graphics g)
    {
        using (Font effectFont = new Font("Segoe UI", 13f, FontStyle.Bold))
        using (Font sleepFont = new Font("Segoe UI", 16f, FontStyle.Bold))
        using (Brush dark = new SolidBrush(Color.FromArgb(115, 81, 42)))
        using (Brush yellow = new SolidBrush(Color.FromArgb(255, 215, 55)))
        using (Brush white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
        {
            if (mood == PetMood.Thinking)
            {
                g.FillEllipse(white, 53, -64, 14, 14);
                g.FillEllipse(white, 67, -82, 22, 22);
                g.DrawString("?", effectFont, dark, 72, -86);
            }
            else if (mood == PetMood.Sleepy)
            {
                g.DrawString("z", effectFont, dark, 52, -55);
                g.DrawString("Z", sleepFont, dark, 70, -78);
            }
            else if (mood == PetMood.Happy)
            {
                DrawStar(g, yellow, -91, -56, 9);
                DrawStar(g, yellow, 83, -38, 7);
            }
            else if (mood == PetMood.Talking)
            {
                float wave = (float)Math.Abs(Math.Sin(tick * 9));
                g.FillEllipse(dark, -17, -91 - wave * 3, 6, 6);
                g.FillEllipse(dark, -3, -95 - wave * 3, 7, 7);
                g.FillEllipse(dark, 12, -91 - wave * 3, 6, 6);
            }
        }
    }

    private static void DrawStar(Graphics g, Brush brush, float x, float y, float size)
    {
        PointF[] points = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double angle = Math.PI * i / 4.0;
            float radius = i % 2 == 0 ? size : size * 0.35f;
            points[i] = new PointF(x + (float)Math.Cos(angle) * radius, y + (float)Math.Sin(angle) * radius);
        }
        g.FillPolygon(brush, points);
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

internal sealed class ChatFlowPanel : FlowLayoutPanel
{
    private const int SbVert = 1;

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

    public ChatFlowPanel()
    {
        DoubleBuffered = true;
    }

    public void HideNativeScrollBar()
    {
        if (IsHandleCreated)
            ShowScrollBar(Handle, SbVert, false);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        HideNativeScrollBar();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        HideNativeScrollBar();
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
