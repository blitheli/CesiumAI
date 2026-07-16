[Sandcastle Copilot](https://cesium.com/blog/2026/07/07/introducing-cesiumjs-sandcastle-copilot/) 的设计完美地结合了**轻量化的 AI 协作**与 **安全的前端代码执行沙盒**。

其主要原理与前端代码运行机制可以拆解为以下两个核心部分：

---

## 一、 Sandcastle Copilot 的主要原理

Sandcastle Copilot 的本质是一个**上下文感知（Context-Aware）的 BYOK 聊天助手**。它的运行原理主要分为三个步骤：

### 1. 上下文收集与构建 (Context Gathering)

当你在对话框中提问或发送指令时，Copilot 不仅仅发送你的提问，它还会自动在后台收集当前编辑器的**上下文状态**：

* **当前代码**：编辑器里正在编写的 JavaScript、HTML 和 CSS 代码。
* **运行状态与控制台日志**：当前运行环境下是否有报错（Console Errors）、警告或 runtime 状态。
* **API 知识库**：结合了 CesiumJS 专有的 API 文档。

### 2. BYOK（Bring-Your-Own-Key）直连请求

* **无中转通信**：Copilot 获得上下文和你的 prompt（提示词）后，会直接通过前端向你配置的第三方大模型供应商（如 OpenAI、Anthropic 等）发起 API 请求。
* **隐私安全**：所有的 API 密钥、Prompt 和对话记录都留在你的浏览器本地或直连通道中，Cesium 官方服务器**不会**接触、收集或存储这些敏感数据。

### 3. 代码差异比对 (Diff Check) 与应用

* 大模型返回新代码后，Copilot 不会直接覆盖你的原代码，而是通过前端的 **Diff 引擎**（类似 Git diff）在聊天面板中展示代码的修改对比。
* 你可以直观地看到哪些行被修改了，确认无误后点击“Apply（应用）”，代码才会被写入主编辑器。

---

## 二、 如何在前端实现并运行代码？

[CesiumJS Sandcastle](https://sandcastle.cesium.com/) 运行用户实时编写的 3D 渲染代码，采用了前端开发中非常经典的“编辑器 + 隔离沙盒 (Iframe)”架构：

### 1. 核心架构：主页面与沙盒的解耦

Sandcastle 的界面分为两部分：

* **左侧/主页面**：代码编辑器（通常使用 Monaco Editor 或 CodeMirror）和 Copilot 面板。
* **右侧/预览区**：一个专门的 `<iframe>`（在 Sandcastle 中被称为 **Bucket Frame**，即 `bucketFrame`）。

### 2. 动态生成并注入代码

当用户点击 "Run"（运行）或 Copilot 应用了新代码时，主页面并不会在当前页面直接执行代码，而是执行以下操作：

1. **提取代码**：获取编辑器中最新的 HTML、CSS 和 JavaScript 文本。
2. **模板拼接**：Sandcastle 拥有一个基础模板，它会将 CesiumJS 的库文件（如 `Cesium.js`、样式表）以及一些初始化脚本（比如构建起底图和控制台拦截器的 `Sandcastle-client.js`）与用户编写的代码拼接在一起。
3. **重写 Iframe 内容**：
通过动态修改 `<iframe>` 的内容来加载新代码。通常有以下两种前端实现手段：
* **Data URI / Blob URL**：将拼接好的 HTML 代码转换为 Blob 对象，生成一个唯一的 URL 赋值给 iframe：
```javascript
const blob = new Blob([htmlContent], { type: 'text/html' });
iframe.src = URL.createObjectURL(blob);

```


* **document.write() 直接写入**：直接清空并重写子 iframe 的 document：
```javascript
const iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
iframeDoc.open();
iframeDoc.write(htmlContent);
iframeDoc.close();

```





### 3. 三维引擎的生命周期管理与重构

因为 CesiumJS 依赖 WebGL 进行 3D 渲染，频繁运行代码如果只是单纯追加脚本，会导致 WebGL 上下文丢失或内存泄漏。

* **彻底销毁与重建**：每次点击 "Run" 时，由于 `<iframe>` 的 `src` 发生了重载，浏览器会自动销毁旧 `iframe` 内的所有 JavaScript 变量、DOM 节点、Event Listeners 以及 **WebGL Context（三维上下文）**。
* 新的 `iframe` 载入时，会从零开始重新初始化 `Cesium.Viewer`，确保每次运行都是一个干净、不受上次污染的运行环境。

### 4. 跨域与安全性保障 (Sandboxing)

为了防止用户编写的恶意代码窃取主页面的敏感数据（或在 Copilot 场景下窃取你的 API Key），Sandcastle 的 `<iframe>` 通常会设置 `sandbox` 属性：

```html
<iframe id="bucketFrame" sandbox="allow-scripts allow-same-origin allow-popups"></iframe>

```

* 这既保证了代码可以正常执行 JavaScript 和渲染 3D 场景，又限制了运行中的代码去非法访问主页面的全局变量和 LocalStorage。