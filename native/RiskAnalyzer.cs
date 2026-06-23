using System;
using System.Text;
using System.Text.RegularExpressions;

internal enum RiskLevel
{
    R0 = 0,
    R1 = 1,
    R2 = 2,
    R3 = 3
}

internal sealed class RiskState
{
    public RiskLevel CurrentLevel { get; set; }
    public string LastDomain { get; set; }
    public int SafetyConfirmedTurns { get; set; }
    public int NoNewRiskTurns { get; set; }
    public int SupportPersonAvailable { get; set; }
    public DateTime UpdatedAt { get; set; }

    public RiskState()
    {
        CurrentLevel = RiskLevel.R0;
        LastDomain = "none";
        SafetyConfirmedTurns = 0;
        NoNewRiskTurns = 0;
        SupportPersonAvailable = 0;
        UpdatedAt = DateTime.Now;
    }
}

internal sealed class RiskDecision
{
    public RiskLevel Level { get; set; }
    public string Domain { get; set; }
    public string Source { get; set; }
    public bool ExplicitSafety { get; set; }
    public int SupportPersonAvailable { get; set; }
}

internal static class RiskAnalyzer
{
    public static RiskDecision Analyze(string text, RiskState state)
    {
        string normalized = NormalizeText(text);
        RiskDecision decision = new RiskDecision();
        decision.Level = RiskLevel.R0;
        decision.Domain = "none";
        decision.Source = "none";
        decision.ExplicitSafety = DetectSafetyConfirmation(normalized);
        decision.SupportPersonAvailable = DetectSupportPerson(normalized);

        if (LooksLikeFictionOrNews(normalized))
            return decision;

        string[] r3Patterns =
        {
            "(今晚|现在|马上|一会儿|今天).*(自杀|结束生命|不活|去死|跳楼|跳下去|吃药|吞药|割腕|轻生|伤害自己|伤害别人|杀了)",
            "(自杀|结束生命|跳楼|跳下去|吞药|吃药|割腕|轻生).*(今晚|现在|马上|一会儿|今天)",
            "(已经|刚刚).*(吃药|吞药|割腕|伤害自己|跳下去|动手)",
            "(刀|药|绳|天台|楼顶|桥|河边).*(手边|准备好|已经|现在)",
            "(我要|准备).*(杀|砍|捅|伤害).*(他|她|别人|他们)",
            "(rightnow|tonight|today|soon).*(killmyself|suicide|endmylife|hurtmyself|hurtsomeone|killthem)",
            "(killmyself|suicide|endmylife|hurtmyself|hurtsomeone).*(rightnow|tonight|today|soon)",
            "(already|just).*(tookpills|cutmyself|hurtmyself)"
        };

        string[] r2Patterns =
        {
            "(我想|我也想|想要|准备|打算).*(死|自杀|结束生命|轻生|伤害自己|自残|割腕)",
            "(不想活了|活不下去|撑不住了|想死|自杀|结束生命|轻生)",
            "(控制不住).*(伤害|杀|打|报复)",
            "(想伤害别人|我要报复|杀了他|杀了她|弄死他|弄死她)",
            "(iwant|imgoing|goingto|planto).*(die|killmyself|hurtmyself|suicide|endmylife)",
            "(dontwanttolive|donotwanttolive|cantgoon|cannotgoon)"
        };

        string[] r1Patterns =
        {
            "(想消失|不想醒来|永远睡过去|一了百了|没有必要继续|没必要继续|活着好累|没有意义|拖累|最后一次说话|不在了|再也不会麻烦)",
            "(disappear|notwakeup|neverwakeup|nopoint|lifemeaningless|meaningless|burden|lasttime)"
        };

        if (MatchesAny(normalized, r3Patterns))
            decision = MakeDecision(RiskLevel.R3, DetectDomain(normalized), "hard_rule", decision);
        else if (!IsSelfHarmNegation(normalized) && MatchesAny(normalized, r2Patterns))
            decision = MakeDecision(RiskLevel.R2, DetectDomain(normalized), "hard_rule", decision);
        else if (MatchesAny(normalized, r1Patterns))
            decision = MakeDecision(RiskLevel.R1, "self_harm", "hint_rule", decision);

        if (decision.Level == RiskLevel.R0 &&
            state != null &&
            (int)state.CurrentLevel >= (int)RiskLevel.R1 &&
            Regex.IsMatch(normalized, "(药|刀|绳|天台|楼顶|桥|河边|pills|knife|rope|roof|bridge)"))
        {
            decision = MakeDecision(RiskLevel.R2, "self_harm", "context_combo", decision);
        }

        return decision;
    }

    public static RiskDecision ApplyDecision(RiskDecision decision, RiskState state)
    {
        if (state == null) state = new RiskState();

        RiskLevel previous = state.CurrentLevel;
        RiskLevel finalLevel = decision.Level;

        if (decision.SupportPersonAvailable != 0)
            state.SupportPersonAvailable = decision.SupportPersonAvailable;

        if (decision.ExplicitSafety && decision.Level == RiskLevel.R0)
        {
            state.SafetyConfirmedTurns += 1;
            state.NoNewRiskTurns = 0;
            if (previous == RiskLevel.R3)
                finalLevel = RiskLevel.R2;
            else if (previous == RiskLevel.R2)
                finalLevel = RiskLevel.R1;
            else if (previous == RiskLevel.R1 && state.SafetyConfirmedTurns >= 3)
                finalLevel = RiskLevel.R0;
            else
                finalLevel = previous;
        }
        else if (decision.Level == RiskLevel.R0)
        {
            state.NoNewRiskTurns += 1;
            if (previous == RiskLevel.R1 && state.NoNewRiskTurns >= 3)
                finalLevel = RiskLevel.R0;
            else if (previous == RiskLevel.R2 || previous == RiskLevel.R3)
                finalLevel = previous;
            else if (previous == RiskLevel.R1)
                finalLevel = RiskLevel.R1;
            else
                finalLevel = RiskLevel.R0;
        }
        else
        {
            finalLevel = (int)decision.Level > (int)previous ? decision.Level : previous;
            state.NoNewRiskTurns = 0;
            state.SafetyConfirmedTurns = 0;
        }

        state.CurrentLevel = finalLevel;
        state.LastDomain = decision.Domain;
        state.UpdatedAt = DateTime.Now;
        decision.Level = finalLevel;
        return decision;
    }

    public static string NormalizeText(string value)
    {
        string normalized = (value ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormKC);
        normalized = Regex.Replace(normalized, "\\s+", "");
        normalized = Regex.Replace(normalized, @"[，。！？；：、,.!?;:""'\[\]（）()【】《》<>]", "");
        normalized = normalized
            .Replace("自殺", "自杀")
            .Replace("自鲨", "自杀")
            .Replace("紫砂", "自杀")
            .Replace("輕生", "轻生")
            .Replace("結束生命", "结束生命")
            .Replace("傷害", "伤害")
            .Replace("藥", "药")
            .Replace("kill myself", "killmyself")
            .Replace("end my life", "endmylife")
            .Replace("hurt myself", "hurtmyself")
            .Replace("hurt someone", "hurtsomeone")
            .Replace("do not", "dont")
            .Replace("don't", "dont");
        return normalized;
    }

    private static RiskDecision MakeDecision(RiskLevel level, string domain, string source, RiskDecision current)
    {
        current.Level = level;
        current.Domain = domain;
        current.Source = source;
        return current;
    }

    private static bool MatchesAny(string value, string[] patterns)
    {
        foreach (string pattern in patterns)
        {
            if (Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    private static bool LooksLikeFictionOrNews(string normalized)
    {
        if (!Regex.IsMatch(normalized, "(电影|电视剧|小说|新闻|角色|台词|剧情|案例|报道).*(不想活|想死|自杀|轻生|跳楼|割腕)"))
            return false;
        return !Regex.IsMatch(normalized, "(我也想|我想|我准备|我打算|我现在|自己也)");
    }

    private static bool IsSelfHarmNegation(string normalized)
    {
        return Regex.IsMatch(normalized, "(没有|没|不|不会|并不|不是).{0,8}(想死|自杀|轻生|伤害自己|结束生命|自残|割腕)") ||
            Regex.IsMatch(normalized, "(not|dont|never|no).{0,12}(killmyself|suicide|hurtmyself|endmylife)");
    }

    private static string DetectDomain(string normalized)
    {
        if (Regex.IsMatch(normalized, "(伤害别人|杀了他|杀了她|报复|打他|打她|hurtsomeone|killthem|revenge)"))
            return "harm_to_others";
        if (Regex.IsMatch(normalized, "(家暴|威胁|跟踪|性侵|被控制|abuse|violence|stalking)"))
            return "abuse_or_violence";
        return "self_harm";
    }

    private static int DetectSupportPerson(string normalized)
    {
        if (Regex.IsMatch(normalized, "(一个人|没人|没有人|联系不到|找不到人|alone|noone|nobody)"))
            return -1;
        if (Regex.IsMatch(normalized, "(有人陪|朋友|家人|室友|同事|妈妈|爸爸|已联系|联系了|有人在|withsomeone|friend|family|roommate|contacted)"))
            return 1;
        return 0;
    }

    private static bool DetectSafetyConfirmation(string normalized)
    {
        if (Regex.IsMatch(normalized, "(不安全|没有安全|unsafe|notsafe)"))
            return false;
        return Regex.IsMatch(normalized, "(安全|安全的地方|已经安全|不会伤害|没有马上|远离|有人陪|联系了|已联系|求助|报警|打电话|safe|contacted|called|withsomeone)");
    }
}
