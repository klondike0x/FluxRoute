<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./assets/FluxRoute-white.svg">
  <source media="(prefers-color-scheme: light)" srcset="./assets/FluxRoute-dark.svg">
  <img width="650" alt="FluxRoute AI Logo" src="./assets/FluxRoute-dark.svg" />
</picture>

# FluxRoute AI `v1.6.2`

### ⚡ Intelligent Swiss Army Knife for DPI Bypass on Windows

**A self-learning system that unifies Zapret, ByeDPI, and Cloudflare Warp into a single adaptive mechanism.**

[🇷🇺 Русская версия](README.md) | [📥 Download Release](https://github.com/mx57/FluxRoute_AI/releases) | [🐛 Report an Issue](https://github.com/mx57/FluxRoute_AI/issues)

---

[![Stars](https://img.shields.io/github/stars/mx57/FluxRoute_AI?style=for-the-badge&logo=github&color=FFD700)](https://github.com/mx57/FluxRoute_AI)
[![Releases](https://img.shields.io/github/v/release/mx57/FluxRoute_AI?include_prereleases&sort=semver&logo=github&label=version&style=for-the-badge&color=3FB950)](https://github.com/mx57/FluxRoute_AI/releases)
[![Downloads](https://img.shields.io/github/downloads/mx57/FluxRoute_AI/total?logo=github&label=downloads&style=for-the-badge&color=4FC3F7)](https://github.com/mx57/FluxRoute_AI/releases)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&style=for-the-badge)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](./LICENSE)

</div>

---

## 🌟 Why FluxRoute AI?

Typical GUIs for DPI tools just launch processes. **FluxRoute AI** goes further — it **thinks**. Censorship and DPI filters change constantly, and what worked yesterday might fail today. Our system uses mathematical models to automatically adapt to your specific ISP and network.

### 🧠 Artificial Intelligence (Thompson Sampling)
Instead of manually guessing which profile to use, the AI Orchestrator analyzes the success of every connection attempt. It balances between using the current "Gold Standard" strategy and exploring new, potentially more effective parameter combinations.

### 🧬 Genetic Evolution
The system literally "grows" new BAT profiles. it crosses parameters of the most successful strategies, applies random mutations (desync, split, fake-tls), and verifies the results. Only the fittest configurations survive.

---

## 🛠 Key Features

| Category | Features |
| :--- | :--- |
| **Engine Support** | Zapret (`winws.exe`), ByeDPI (`ciadpi.exe`), Cloudflare Warp (`warp-plus.exe`). |
| **Operating Modes** | **Standalone** (single engine), **Hybrid** (smart Zapret/ByeDPI toggle), **Parallel** (multiple engines), **Chained** (SOCKS5 tunnel chain). |
| **Intelligence** | Thompson Sampling, Wilson Lower Bound (confidence intervals), Fast Start (instant warm-up of TOP strategies). |
| **Networking** | Per-network AI policy (Network Fingerprinting), Auto-MTU discovery for Warp. |
| **Automation** | Full lifecycle: from Warp account registration to automated binary updates from GitHub. |

---

## 🚀 New in v1.6.2

> [!IMPORTANT]
> This is a major update focused on the synergy between traditional DPI tools and modern VPN technologies.

- **🌐 Cloudflare Warp (WireGuard/AmneziaWG):** Native integration. Use Warp as standalone protection or as a "tunnel-in-a-tunnel" for Zapret.
- **⚡ Chaining Mode:** Run Zapret/ByeDPI with traffic routed through Warp. This bypasses filters based on both packet signatures and IP address lists.
- **🎯 Wilson Score 2.0:** Refined ranking logic. The AI now more accurately weighs strategy reliability using a 14-day rolling history.
- **🧪 Deep Mutations:** Genetic evolution now explores `DesyncAnyProtocol`, `DesyncFooling`, and `FakeResend` parameters.

---

## 📸 UI Gallery

<div align="center">
  <table border="0">
    <tr>
      <td><img src="https://github.com/user-attachments/assets/70dda58d-cbf3-43f8-b8ae-72b7fad3d88e" width="400" alt="Main UI" /><br/><p align="center"><i>Main Control Screen</i></p></td>
      <td><img src="https://github.com/user-attachments/assets/bf33cffb-6d56-4055-8f8e-8c807f57d9a7" width="400" alt="AI Stats" /><br/><p align="center"><i>AI Statistics & Evolution</i></p></td>
    </tr>
  </table>
</div>

---

## ⚙️ Mode Comparison

| Mode | Best For | ISP Evasion Difficulty |
| :--- | :--- | :--- |
| **Zapret** | YouTube, Discord, basic bypass. | Low (easily fingerprinted) |
| **Warp** | IP-based blocks (Instagram, Twitter). | Medium (blocked by port/protocol) |
| **Hybrid** | When ISP blocks change protocols randomly. | High |
| **Chained** | Maximum penetration (DPI Bypass + VPN). | **Maximum** |

---

## 📅 Roadmap (Future)

- [ ] **Sing-Box Integration:** Support for VLESS, Reality, and other cutting-edge protocols.
- [ ] **P2P Genome Sharing:** Optionally share successful "mutations" with the community anonymously.
- [ ] **Traffic Visualization:** Real-time throughput and bypass efficiency graphs.

---

## ⚠️ Security & WinDivert

This project uses the **WinDivert** driver to modify network packets on-the-fly.
- This is **not a virus**. It is a system administration tool.
- Antivirus software (e.g., Kaspersky, Defender) may flag it as `HackTool` or `RiskTool`.
- **Solution:** Add the application folder to your whitelist (exclusions).

---

## 🙏 Acknowledgments

- **[klondike0x/FluxRoute](https://github.com/klondike0x/FluxRoute)** — Foundation and architecture.
- **[bol-van/zapret](https://github.com/bol-van/zapret)** — The legendary bypass core.
- **[hiddify/warp-plus](https://github.com/hiddify/warp-plus)** — The best CLI Warp implementation.

---

<div align="center">

**Developed by the community for a free internet.**

[mx57](https://github.com/mx57) © 2026. Licensed under GPLv3.

**[⭐ Star this repo if it helped you!](https://github.com/mx57/FluxRoute_AI)**

</div>
