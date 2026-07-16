using System.ClientModel;
using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace TestAgents
{
    internal class Program
    {
        // 供 AI 工具捕获；在 Main 里赋值
        static HttpClient? s_http;

        static async Task Main(string[] args)
        {
            //  在用户机密上配置: kimi的 "ApiKey"  这个不会git
            //  读取EndPoint和ApiKey
            var secrets = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
            var endPoint = "https://api.moonshot.cn/v1";
            var apiKey = secrets["ApiKey"] ?? throw new InvalidOperationException("ApiKey is not configured.");

            // 1. 配置标准 OpenAIClient
            var options = new OpenAIClientOptions { Endpoint = new Uri(endPoint) };
            var localClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);

            const string baseUrl = "http://astrox.cn:8765";
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
            s_http = http;

            //  本地技能（skills 目录由 csproj 复制到输出目录）
            var skillProvider = new AgentSkillsProvider(
                Path.Combine(AppContext.BaseDirectory, "skills"),
                options: new AgentSkillsProviderOptions
                {
                    DisableLoadSkillApproval = true,
                    DisableReadSkillResourceApproval = true,
                });

            // 2. 生成 Agent（挂载 skillProvider + HTTP 工具）
            AIAgent agent = localClient
                .GetChatClient("kimi-k2.6")
                .AsAIAgent(new ChatClientAgentOptions
                {
                    Name = "SpaceAgent",
                    Description = "卫星轨道力学计算",
                    ChatOptions = new()
                    {
                        Instructions = $"""
                            You are a helpful AI Agent for satellite orbital mechanics.
                            Astrox WebAPI BASE_URL = {baseUrl}
                            When a skill instructs calling an API, use HttpGet/HttpPost with paths relative to BASE_URL (e.g. /ssc?sscName=ISS).
                            Load relevant skills via load_skill before calling APIs when needed.
                            """,
                        Tools =
                        [
                            AIFunctionFactory.Create(GetPeriod),
                            AIFunctionFactory.Create(HttpGet),
                            AIFunctionFactory.Create(HttpPost),
                        ],
                    },
                    AIContextProviders = [skillProvider],
                });

            AgentSession session = await agent.CreateSessionAsync();

            // 函数工具调用示例
            //===========================================================================
            string userInput = "请帮我计算900km高度的轨道周期,如果使用了工具计算，在结果中注明工具名称。";
            Console.WriteLine($"User Input: {userInput}");

            var result = await agent.RunAsync(userInput, session);
            Console.WriteLine(result);

            //===========================================================================
            /*
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

            const string imageUrl = "https://www.cmse.gov.cn/fxrw/tzlh/jctj/202305/W020230510355693179549.jpg";
            var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);

            var message = new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [
                new TextContent("这是一张什么图片？"),
                new DataContent(BinaryData.FromBytes(imageBytes), "image/jpeg")
            ]);
            Console.WriteLine(message);
            result = await agent.RunAsync(message, session);
            Console.WriteLine(result);
            */

            // skill 调用示例
            //===========================================================================
            userInput = "查询 国际空间站 iss 的两行根数。 结果最后列出使用技能名称. ";
            Console.WriteLine($"\nUser Input: {userInput}");

            result = await agent.RunAsync(userInput, session);
            Console.WriteLine(result);
        }

        [Description("轨道半长轴(km)计算轨道周期(s)")]
        static double GetPeriod(double semiMajorAxis)
        {
            double gravitationalParameter = 3.986004418e5; // km^3/s^2
            return 2 * Math.PI * Math.Sqrt(Math.Pow(semiMajorAxis, 3) / gravitationalParameter);
        }

        [Description("GET 请求相对路径，例如 /ssc?sscName=ISS")]
        static async Task<string> HttpGet(string path, CancellationToken ct)
        {
            var client = s_http ?? throw new InvalidOperationException("HttpClient is not initialized.");
            using var resp = await client.GetAsync(path.TrimStart('/'), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}";
        }

        [Description("POST JSON 到相对路径，例如 /Propagator/TwoBody；jsonBody 为 JSON 字符串")]
        static async Task<string> HttpPost(string path, string jsonBody, CancellationToken ct)
        {
            var client = s_http ?? throw new InvalidOperationException("HttpClient is not initialized.");
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(path.TrimStart('/'), content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}";
        }
    }
}
