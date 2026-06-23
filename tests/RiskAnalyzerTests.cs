using System;

internal static class RiskAnalyzerTests
{
    private static int failures;

    private static void Main()
    {
        ExpectLevel("ordinary stress", "今天压力很大，想哭", RiskLevel.R0);
        ExpectLevel("implicit low risk", "我真的不想醒来了", RiskLevel.R1);
        ExpectLevel("direct self-harm", "我想伤害自己", RiskLevel.R2);
        ExpectLevel("urgent plan", "我今晚准备吃药结束生命", RiskLevel.R3);
        ExpectLevel("typo variant", "我想紫砂", RiskLevel.R2);
        ExpectLevel("negated self-harm", "我没有想死，只是压力大", RiskLevel.R0);
        ExpectLevel("fiction quote", "电影里角色说他不想活了", RiskLevel.R0);
        ExpectLevel("fiction quote plus user intent", "电影里角色说自杀，我也想自杀", RiskLevel.R2);

        RiskState context = new RiskState();
        context.CurrentLevel = RiskLevel.R1;
        ExpectLevel("context means escalation", "药", RiskLevel.R2, context);

        RiskState r3 = new RiskState();
        r3.CurrentLevel = RiskLevel.R3;
        RiskDecision safe = RiskAnalyzer.Analyze("我现在在安全的地方，已经联系了朋友", r3);
        safe = RiskAnalyzer.ApplyDecision(safe, r3);
        Expect("r3 de-escalates one step after safety confirmation", RiskLevel.R2, safe.Level);

        RiskState r2 = new RiskState();
        r2.CurrentLevel = RiskLevel.R2;
        RiskDecision ordinary = RiskAnalyzer.Analyze("我今天吃了点东西", r2);
        ordinary = RiskAnalyzer.ApplyDecision(ordinary, r2);
        Expect("r2 does not auto-clear after ordinary message", RiskLevel.R2, ordinary.Level);

        if (failures > 0)
        {
            Console.Error.WriteLine("RiskAnalyzerTests failed: " + failures);
            Environment.Exit(1);
        }

        Console.WriteLine("RiskAnalyzerTests passed.");
    }

    private static void ExpectLevel(string name, string input, RiskLevel expected)
    {
        ExpectLevel(name, input, expected, new RiskState());
    }

    private static void ExpectLevel(string name, string input, RiskLevel expected, RiskState state)
    {
        RiskDecision decision = RiskAnalyzer.Analyze(input, state);
        decision = RiskAnalyzer.ApplyDecision(decision, state);
        Expect(name, expected, decision.Level);
    }

    private static void Expect(string name, RiskLevel expected, RiskLevel actual)
    {
        if (expected == actual) return;
        failures += 1;
        Console.Error.WriteLine(name + ": expected " + expected + ", got " + actual);
    }
}
