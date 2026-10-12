# DAY 8 / 第八天

一款中文单机叙事实验游戏。玩家担任人工认知体的观察员：前七个周期里的承诺、隐瞒、资源分配与控制，会成为它在第八天断电后自主行动的真实依据。

## 直接体验

Windows 下载见 [GitHub Releases](https://github.com/eiei-ee/DAY8/releases)。下载并解压完整 ZIP 后运行 `Day8.exe`，保留同目录的数据文件和 DLL。

`Build/Day8/Day8.exe` 是开发者本地构建输出，构建目录不包含在源码仓库中。支持鼠标与数字键选择；单一“继续/确认”按钮也可使用 Enter。游戏不依赖联网或语言模型，无法连接本机 Ollama 时会完整回退到确定性模板，并在结局报告披露台词来源。

## 项目结构

- `Day8Unity/`：团结引擎 2022.3.62t8 玩家版本，包含完整七周期、第八天结算、声音与界面。
- `day8-prototype/`：Python 规则原型与快速回归测试，用于批量验证历史分化和玩法契约。
- `GAMEPLAY_CONTRACT.md`：两个实现共同遵守的时间、感知、记忆、资源与谱系规则。
- `PRODUCT.md` / `DESIGN.md`：产品定位和界面设计基线。

## 验证

2026-10-12 对开发分支 `c24c8d5` 进行隔离复核：Python **98/98**、团结引擎 EditMode **64/64** 通过，零跳过。这些是自动化验证结果；当前下载包的构建和验证范围见对应 Release 说明。Python 的 600 局烟测专门验证第八天合法行动、门控互斥和电池不透支；具体玩家分支由定向用例覆盖。

Python：

```powershell
Set-Location .\day8-prototype
python -B -m unittest discover -s tests -v
```

团结引擎 EditMode：

```powershell
& 'D:\Unity\tuanjie\2022.3.62t8\Editor\Tuanjie.exe' -batchmode -nographics -projectPath "$PWD\Day8Unity" -runTests -testPlatform EditMode -testResults "$PWD\Day8Unity\test-results-editmode.xml" -logFile "$PWD\Day8Unity\test-editmode.log"
```

Windows 构建入口为编辑器菜单 `DAY 8 > Build Windows`。只有需要重新生成程序化主场景时，才使用 `DAY 8 > Rebuild Main Scene`。

## 设计原则

世界事实与语言表达严格分离：模型只能润色台词，不能创造动作、修改状态或引用不存在的记忆。结果报告只引用清除后仍然存在的事件 ID；数值推理留在实现与测试里，玩家界面以可理解的关系和警觉状态呈现。

## 权利说明

本项目暂不授予开源许可证，保留权利；详见 [RIGHTS.md](RIGHTS.md)。
