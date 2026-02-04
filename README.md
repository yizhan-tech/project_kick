![Banner](repo_media/repo_banner.png)

# README

`Project KICK` is an open repository focused on **football (soccer) analytics and applied science**, with an emphasis on clear methodology and reproducible analysis.

The goal of this project is to explore how ideas from **data science, computer science, mathematics, and physics** can be used to better understand football.

## 1. Repository Structure

### 1.1 Football Metrics

This module contains implementations and experiments around football analytics metrics that aim to quantify tactical structure, player contribution, and contextual game value.

| Name | File Link | Data Input | Status |
| :--- | :--- | :--- | :--- | 
| Expected Goals (xG) | [`xG.ipynb`](./football_metrics/shooting/xG.ipynb) | Event | 🟢 Demo Available |
| Expected Threat (xT) | [`xT.ipynb`](./football_metrics/progression/xT.ipynb) | Event | 🟢 Demo Available |
| Pitch Control | [`pitch_control.ipynb`](./football_metrics/movement/pitch_control.ipynb) | Event | 🟢 Demo Available |
| Passing Network | N/A | Event | 🟡 Developing |
| Physical Analysis | N/A | Tracking | 🔴 Coming Soon... |

### 1.2 football_animations

This module contains reusable animation primitives and scene templates for explaining football concepts in a precise, visual, and educational way.

<table style="width: 100%; border-collapse: collapse;">
  <tr>
    <td align="center" width="33%">
      <b>Voronoi Pitch Control Helper</b><br>
      <img src="/home/liyi/Documents/project_kick_github/repo_media/voronoi_example.gif" width="100%" alt="One by one intro">
    </td>
    <td align="center" width="33%">
      <b>Expected Threat (xT) Helper</b><br>
      <img src="path/to/your/direct_display.gif" width="100%" alt="Direct display">
    </td>
    <td align="center" width="33%">
      <b>Match Flow</b><br>
      <img src="path/to/your/match_flow.gif" width="100%" alt="Match flow animation">
    </td>
  </tr>
</table>

### 1.3 football_simulations

This module contains simulation and control, with reinforcement learning (RL) environments that allow agents to learn football behaviors from reward signals and environment dynamics. A detailed documentation can be found [here](./football_simulations/documentation.md).

## 2. Sources and attribution

This project builds on a combination of **open datasets** and, in some cases, **data accessed under non-disclosure agreements (NDA)**. Only analyses and results that are permitted for public release are included in this repository.

The methodologies used throughout the project vary in origin. Some components are based on established academic papers or prior work in the football analytics community, others are original contributions, and many are the result of combining existing ideas with new modeling or implementation choices.

Attribution is treated as an ongoing process. As the project evolves, references and credits will be continuously updated to acknowledge the original sources and contributors that inform this work.

### 2.1 Data Sources

| Source Name | Data Type | Access Level | Attribution / Link |
| :--- | :--- | :--- | :--- |
| Statsbomb Open Data | Event | Public | [Github Page](https://github.com/statsbomb/open-data) |
| Metrica Sample Data | Event + Tracking | Public | [Github Page](https://github.com/metrica-sports/sample-data/tree/master) |
| SONY Hawkeye Data | Tracking | NDA | [Official Website](https://www.hawkeyeinnovations.com/data) |

### 2.2 Academic Foundations

| Metric / Model | Original Paper / Author | Link / Citation |
| :--- | :--- | :--- | 
| Expected Threat (xT) | Karun Singh | [Introducing Expected Threat (xT)](https://karun.in/blog/expected-threat.html) |
| Pitch Control | Javier Fernandez & Luke Bornn | [Wide Open Spaces: A statistical technique for measuring space creation in professional soccer](https://www.lukebornn.com/papers/fernandez_ssac_2018.pdf) |

## 3. Acknowledgments
Special thanks to the following organizations for their support through data access and technical guidance:
- **Baidu AI Cloud** 
- **Bilibili** 
