using CesiumAI.Api.Services;
using FluentAssertions;

namespace CesiumAI.Api.Tests.Services;

public class AgentInstructionsTests
{
    [Fact]
    public void Text_ContainsRequiredSceneSafetyAndGenericOrbitPolicies()
    {
        AgentInstructions.Text.Should().Contain("场景变更").And.Contain("场景工具");
        AgentInstructions.Text.Should().Contain("可执行 CZML").And.Contain("助手文本");
        AgentInstructions.Text.Should().Contain("纯问题").And.Contain("不调用场景工具");
        AgentInstructions.Text.Should().Contain("简洁中文");

        // 非 J2 轨道必须先加载 skill，再走通用传播 Tool。
        AgentInstructions.Text.Should().Contain("skill");
        AgentInstructions.Text.Should().Contain("PropagateAndAddSatellite");

        // 禁止大型 Position 经模型往返。
        AgentInstructions.Text.Should().Contain("Position");
        AgentInstructions.Text.Should().MatchRegex(
            "(?i)(禁止|不得|不要).{0,24}(大型)?Position.{0,24}(往返|返回模型|模型)");

        // AddSatelliteJ2 不再是唯一建星途径。
        AgentInstructions.Text.Should().NotContain("唯一途径");
    }

    [Fact]
    public void Text_DocumentsIssDefaultLoadSkillThenHttpGetThenPropagate()
    {
        // ISS 默认顺序：先 load SGP4/TLE skill → HttpGet 查询 NORAD 25544 最新 TLE → PropagateAndAddSatellite，24h/60s。
        AgentInstructions.Text.Should().Contain("25544");
        AgentInstructions.Text.Should().Contain("HttpGet");
        AgentInstructions.Text.Should().Contain("TLE");
        AgentInstructions.Text.Should().Contain("SGP4");
        AgentInstructions.Text.Should().Contain("PropagateAndAddSatellite");
        AgentInstructions.Text.Should().Contain("24");
        AgentInstructions.Text.Should().Contain("60");
        AgentInstructions.Text.Should().MatchRegex("国际空间站|ISS");

        // 在 ISS 规则段落内断言顺序，避免被前文通用 PropagateAndAddSatellite 干扰。
        AgentInstructions.Text.Should().MatchRegex(
            @"国际空间站[\s\S]*?SGP4/TLE[\s\S]*?HttpGet[\s\S]*?25544[\s\S]*?PropagateAndAddSatellite");
    }

    [Fact]
    public void Text_ForbidsPropagationAndSceneOps_WhenTleLookupFails()
    {
        // TLE 查询失败 / 不唯一 / 缺两行根数时：禁止传播且不产生 sceneOps。
        AgentInstructions.Text.Should().MatchRegex("失败|不唯一|缺少|缺两行|两行根数");
        AgentInstructions.Text.Should().MatchRegex(
            "(禁止|不得|不要).{0,40}(传播|Propagate)|不产生\\s*sceneOps|不得产生\\s*sceneOps");
        AgentInstructions.Text.Should().Contain("sceneOps");
    }
}
