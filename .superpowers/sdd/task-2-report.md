# Task 2：更新默认 Skills 路径与测试

## 状态

实现完成：默认 `Skills:Path` 已更新为相对 API content root 的 `../astrox-skills/skills`，相对路径解析测试也已按 `api` 与同级 `astrox-skills/skills` 的实际目录布局调整。

## 变更

- `backend/CesiumAI.Api.Tests/Services/AgentFactoryTests.cs`
  - `CreateAsync_ResolvesSkillsRelativeToContentRoot_WithoutCallingRemoteServices` 创建 `astrox-skills/skills` 临时目录，并传入 `../astrox-skills/skills`。
- `backend/CesiumAI.Api/Configuration/SkillsOptions.cs`
  - 默认值从 `../skills` 改为 `../astrox-skills/skills`。
- `backend/CesiumAI.Api/appsettings.json`
  - `Skills:Path` 从 `../skills` 改为 `../astrox-skills/skills`。

未修改 Agent、Tool 或 Astrox HTTP 行为。

## TDD / 验证证据

### 1. 先更新相对路径测试并运行 focused test

命令：

```powershell
dotnet test backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj --filter "FullyQualifiedName~CreateAsync_ResolvesSkillsRelativeToContentRoot"
```

结果：PASS（退出码 0）

```text
已通过! - 失败:     0，通过:     1，已跳过:     0，总计:     1
```

### 2. 完整后端测试套件

命令：

```powershell
dotnet test backend/CesiumAI.Api.Tests/CesiumAI.Api.Tests.csproj
```

结果：FAIL（退出码 1，103 项中 87 通过、16 失败）

```text
失败!  - 失败:    16，通过:    87，已跳过:     0，总计:   103
```

失败不由本任务改动的默认路径字符串直接导致：

- 2 项 `ScenePromptBuilderTests` 在 Windows 上因预期 LF 与实际 CRLF 换行符不一致失败。
- 14 项 `ChatControllerTests` / `SkillsStartupValidationTests` 在 `ApiFactory` 将位于 C: 临时目录的 skills 路径转成绝对路径后，被既有“`Skills:Path` 必须相对于 content root”校验拒绝：

```text
Skills:Path must be relative to the application content root. (Parameter 'Path')
```

该测试夹具问题位于 `backend/CesiumAI.Api.Tests/ApiFactory.cs`，不属于任务 brief 指定的三个修改文件；本任务未扩展范围修复。

### 3. 真实 content root 默认路径烟测

命令：

```powershell
$root = Resolve-Path backend/CesiumAI.Api
$skills = Resolve-Path (Join-Path $root "..\astrox-skills\skills")
Test-Path $skills
```

结果：PASS（退出码 0）

```text
True
```

### 4. 静态检查

修改的三个文件无 IDE linter 诊断。

## 注意事项

若本机 User Secrets 覆盖了 `Skills:Path` 并仍指向旧 `../skills`，应更新或删除该覆盖值；配置优先级会高于 `appsettings.json`。
