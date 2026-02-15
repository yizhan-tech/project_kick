import numpy as np
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon
from mplsoccer import Pitch
import torch
from scipy.interpolate import RegularGridInterpolator
from matplotlib.lines import Line2D
import torch.nn.functional as F
import seaborn as sns

def plot_influence_radius_relation(radius_func, player_pos=np.array([0, 0]), max_dist=30):
    """
    Visualize the functional relationship between player influence radius 
    and spatial distance to the ball.
    """
    distances = np.linspace(0, max_dist, 100)
    ball_positions = np.array([[d, 0] for d in distances])
    
    influence_radii = np.array([
        radius_func(ball_pos, player_pos) for ball_pos in ball_positions
    ])

    plt.figure(figsize=(8, 5))
    plt.plot(distances, influence_radii, color='brown', linewidth=3, alpha=0.7)
    plt.xlabel("Distance to the ball (m)")
    plt.ylabel("Influence Radius (m)")
    plt.title(f"Influence Radius Relation: {radius_func.__name__}")
    plt.ylim(0, 13)
    plt.grid(True, linestyle='--', alpha=0.6)
    plt.show()

def plot_influence_subplot(ax, X, Y, influence, player_pos, ball_pos, player_vel, title):
    """
    Render a specific spatial influence scenario on a provided axes object.
    """
    c = ax.contourf(X, Y, influence, cmap="coolwarm", levels=10, vmin=0, vmax=1)
    
    ax.scatter(*player_pos, color="blue", edgecolors="black", s=100, label="Player", zorder=3)
    ax.scatter(*ball_pos, color="white", edgecolors="black", s=50, label="Ball", zorder=3)
    
    scale = 0.5 
    ax.arrow(player_pos[0], player_pos[1], 
             player_vel[0] * scale, player_vel[1] * scale, 
             color="blue", head_width=0.6, head_length=0.6, linewidth=2, alpha=0.8, zorder=4)
    
    ax.set_title(title)
    ax.set_xlabel("X (m)")
    ax.set_ylabel("Y (m)")
    ax.grid(True, linestyle="--", alpha=0.5)
    return c

def run_influence_comparison(influence_func, x_range=(5, 25), y_range=(5, 25), res=100):
    """
    Execute a comparative spatial analysis between stationary and dynamic states.
    """
    x = np.linspace(x_range[0], x_range[1], res)
    y = np.linspace(y_range[0], y_range[1], res)
    X, Y = np.meshgrid(x, y)
    locations = np.dstack((X, Y))

    cases = [
        {"title": "Stationary / Tight Control", "p_pos": np.array([15, 15]), "b_pos": np.array([15, 15]), "p_vel": np.array([0.1, 0])},
        {"title": "Sprinting / Dynamic Reach", "p_pos": np.array([15, 15]), "b_pos": np.array([7, 15]), "p_vel": np.array([4.5, 4.5])}
    ]

    fig, axes = plt.subplots(1, 2, figsize=(14, 6))
    for i, case in enumerate(cases):
        inf_data = influence_func(case["p_pos"], locations, case["p_vel"], case["b_pos"])
        col_map = plot_influence_subplot(axes[i], X, Y, inf_data, case["p_pos"], case["b_pos"], case["p_vel"], case["title"])
        fig.colorbar(col_map, ax=axes[i], fraction=0.046, pad=0.04)

    plt.tight_layout()
    plt.show()

def generate_pitch_control_map(frame, df_home_players, df_away_players, df_ball, compute_influence_func, 
                               pitch_length=105, pitch_width=68, step=1.0, cmap_colors="RdBu", 
                               player_colors=None, show_velocity=True):
    """
    Synthesize a comprehensive pitch control probability surface for a specific match frame.
    """
    if player_colors is None:
        player_colors = {"home": "red", "away": "blue"}

    def _draw_team_dots(ax, df, color):
        ax.scatter(df["x"], df["y"], color=color, s=200, edgecolors="white", linewidth=2, zorder=10)

    def _draw_velocity_arrow(ax, x, y, vx, vy, color):
        mag = np.hypot(vx, vy)
        if mag < 0.1: return 
        ux, uy = vx / mag, vy / mag
        ratio = pitch_width / pitch_length
        px, py = (-uy * ratio / np.hypot(-uy * ratio, ux / ratio)) * 0.5, (ux / ratio / np.hypot(-uy * ratio, ux / ratio)) * 0.5
        cx, cy = x + ux * 0.75, y + uy * 0.75  
        points = [[cx + px, cy + py], [cx - px, cy - py], [cx + ux * (mag * 0.4), cy + uy * (mag * 0.4)]]
        ax.add_patch(Polygon(points, facecolor=color, edgecolor="white", linewidth=1.5, zorder=7))

    pitch = Pitch(pitch_type="wyscout", linewidth=2, line_color="white")
    X_plot, Y_plot = np.meshgrid(np.linspace(0, 100, int(100 / step) + 1), np.linspace(0, 100, int(100 / step) + 1))
    sx, sy = pitch_length / 100.0, pitch_width / 100.0
    locations_m = np.column_stack([(X_plot * sx).ravel(), (Y_plot * sy).ravel()])

    home = df_home_players[df_home_players["frame"] == frame]
    away = df_away_players[df_away_players["frame"] == frame]
    ball_row = df_ball.loc[df_ball["frame"] == frame].iloc[0]
    ball_pos_m = np.array([ball_row["x"] * sx, ball_row["y"] * sy])

    home_infl = compute_influence_func(home, locations_m, ball_pos_m, sx, sy)
    away_infl = compute_influence_func(away, locations_m, ball_pos_m, sx, sy)
    Z = (1 / (1 + np.exp(-(home_infl - away_infl)))).reshape(X_plot.shape)

    fig, ax = plt.subplots(figsize=(16, 10))
    fig.patch.set_alpha(0); ax.patch.set_alpha(0)
    pitch.draw(ax=ax)
    ax.contourf(X_plot, Y_plot, Z, alpha=1, cmap=cmap_colors, levels=24, zorder=-1)

    _draw_team_dots(ax, away, player_colors["away"])
    _draw_team_dots(ax, home, player_colors["home"])

    if show_velocity:
        for team_df, color in [(home, player_colors["home"]), (away, player_colors["away"])]:
            for _, row in team_df.iterrows():
                _draw_velocity_arrow(ax, row['x'], row['y'], row['vx'], row['vy'], color)

    ax.scatter(ball_row["x"], ball_row["y"], s=150, facecolor="yellow", edgecolor="black", linewidth=2, zorder=30)
    plt.title(f"Project Kick | Frame: {frame} | Pitch Control Map", color="white", fontsize=20)
    return fig, ax

def plot_space_value_model_results(model, X_test, y_test, num_samples=3, device="cpu"):
    """
    Evaluate and visualize Neural Network predictions against observed spatial density.
    """
    model.eval()
    indices = np.random.choice(len(X_test), num_samples, replace=False)
    pitch = Pitch(pitch_type='wyscout', line_color='#7c7c7c', goal_type='box')
    fig, axes = plt.subplots(num_samples, 2, figsize=(16, 5.5 * num_samples))
    
    with torch.no_grad():
        for i, idx in enumerate(indices):
            x_input = torch.tensor(X_test[idx], dtype=torch.float32).unsqueeze(0).to(device)
            pred = model(x_input).cpu().numpy().reshape(15, 21)
            actual = y_test[idx].reshape(15, 21)
            ball_x, ball_y = X_test[idx][0], X_test[idx][1]
            
            for j, (data, title) in enumerate([(actual, "Observed Density"), (pred, "Expected Value Surface")]):
                ax = axes[i, j]
                pitch.draw(ax=ax)
                im = ax.imshow(data, extent=[0, 100, 0, 100], origin='lower', cmap='Reds', vmin=0, vmax=1, alpha=0.6, aspect=0.65)
                pitch.scatter(ball_x, ball_y, color='black', s=100, edgecolors='white', linewidth=1.5, ax=ax, zorder=3)
                ax.set_title(f"Sample {idx}: {title}", fontsize=14, pad=10)
                fig.colorbar(im, ax=ax, fraction=0.03, pad=0.04)

    plt.tight_layout(); plt.show()

def plot_pc_vs_q(frame_id, model, df_home, df_away, df_ball, device, pc_func, X, Y, pc_grid):
    """
    Analyze the interplay between defensive probability (PC) and offensive reward (Q).
    """
    model.eval()
    pitch = Pitch(pitch_type='wyscout', line_color='#7c7c7c', goal_type='box')
    _, axes = plt.subplots(1, 2, figsize=(20, 8))

    ball_row = df_ball[df_ball['frame'] == frame_id].iloc[0]
    bx, by = ball_row['x'], ball_row['y']
    
    with torch.no_grad():
        V_raw = model(torch.tensor([[bx, by]], dtype=torch.float32).to(device)).cpu().numpy().reshape(15, 21)

    V_interp = RegularGridInterpolator((np.linspace(0, 100, 15), np.linspace(0, 100, 21)), V_raw, bounds_error=False, fill_value=0)(np.array([Y.ravel(), X.ravel()]).T).reshape(101, 101)

    home_frame, away_frame = df_home[df_home['frame'] == frame_id], df_away[df_away['frame'] == frame_id]
    h_infl = pc_func(home_frame, pc_grid, np.array([bx, by]), 1.0, 1.0)
    a_infl = pc_func(away_frame, pc_grid, np.array([bx, by]), 1.0, 1.0)
    
    PC = (1 / (1 + np.exp(-(h_infl - a_infl)))).reshape(101, 101)
    Q = PC * V_interp

    for i, (surf, title, cmap) in enumerate(zip([PC, Q], ["Pitch Control ($PC$)", "Space Quality ($Q$)"], ['bwr', 'Reds'])):
        pitch.draw(ax=axes[i])
        axes[i].imshow(surf, extent=[0, 100, 0, 100], origin='lower', cmap=cmap, alpha=0.7, aspect='auto')
        pitch.scatter(bx, by, color='white', s=60, edgecolors='black', linewidth=1.5, ax=axes[i], zorder=4)
        pitch.scatter(home_frame.x, home_frame.y, color='red', edgecolors='black', s=80, ax=axes[i], zorder=3)
        pitch.scatter(away_frame.x, away_frame.y, color='blue', edgecolors='black', s=80, ax=axes[i], zorder=3)
        axes[i].set_title(title, fontsize=16, pad=10)

    plt.tight_layout(); plt.show()

def plot_surface_quality_analysis(player_id, t, w, team_df, opponent_df, df_ball, model, pc_grid, device, gi_func, pc_calc_func):
    """
    Perform a multi-temporal analysis of space occupation and quality metrics.
    """
    gi_t = gi_func(player_id, team_df, opponent_df, t, w, model, df_ball, device, pc_grid)
    frames = [t, t + w + 1]
    ball_data = df_ball[df_ball['frame'].isin(frames)].set_index('frame')
    p_data = team_df[(team_df['frame'].isin(frames)) & (team_df['player'] == player_id)].set_index('frame')

    with torch.no_grad():
        V_batch = F.interpolate(model(torch.tensor(ball_data[['x', 'y']].values, dtype=torch.float32).to(device)).reshape(2, 1, 15, 21), 
                                size=pc_grid.shape[:2], mode='bilinear', align_corners=True).squeeze(1).cpu().numpy()

    pitch = Pitch(pitch_type='wyscout', line_color='#7c7c7c', goal_type='box', pitch_color='#1a1a1a')
    fig, axes = plt.subplots(1, 2, figsize=(22, 10), facecolor='#1a1a1a')

    for i, f in enumerate(frames):
        ax = axes[i]; pitch.draw(ax=ax)
        frame_home, frame_away = team_df[team_df['frame'] == f], opponent_df[opponent_df['frame'] == f]
        pc_surf = pc_calc_func(frame_home, frame_away, ball_data.loc[f, ['x', 'y']].values, pc_grid, sx=1.05, sy=0.68)
        
        ax.imshow(pc_surf * V_batch[i], extent=[0, 100, 0, 100], origin='lower', cmap='magma', alpha=0.8, zorder=0, aspect=0.64)
        pitch.scatter(frame_away.x, frame_away.y, s=70, c='#00E5FF', edgecolors='white', ax=ax, alpha=0.6, zorder=2)
        pitch.scatter(frame_home.x, frame_home.y, s=70, c='#FF4B4B', edgecolors='white', ax=ax, alpha=0.6, zorder=2)
        
        target = p_data.loc[f]
        pitch.scatter(target.x, target.y, s=600, facecolors='none', edgecolors='lime', linewidth=3, marker='o', ax=ax, zorder=10) 
        pitch.scatter(ball_data.loc[f].x, ball_data.loc[f].y, s=50, c='white', edgecolors='black', ax=ax, zorder=11)
        ax.set_title(f"Space Quality Q | Frame: {f}", color='white', fontsize=16, pad=15)

    plt.suptitle(f"Space Occupation Analysis for Player {player_id} | $G_i(t) = {gi_t:.6f}$", color='white', fontsize=24, y=0.98)
    plt.tight_layout(); plt.show()

def plot_player_sog_momentum(momentum_data):
    """
    Visualize temporal fluctuations in Space Occupation Gain (G_i) throughout a match.
    """
    if not momentum_data or not momentum_data['windows']: return
    meta = momentum_data['metadata']
    plt.figure(figsize=(24, 8), facecolor='#1a1a1a')
    ax = plt.gca(); ax.set_facecolor('#1a1a1a')

    cat_colors = {'gain': '#00FF00', 'loss': '#FF0000', 'noise': '#808080'}
    all_times = [win['frame'] / (meta['fps'] * 60.0) for win in momentum_data['windows']]
    plt.step(all_times, [win['gi'] for win in momentum_data['windows']], where='post', color='white', linewidth=0.6, alpha=0.1)
    
    for win in momentum_data['windows']:
        plt.scatter(win['frame'] / (meta['fps'] * 60.0), win['gi'], color=cat_colors[win['category']],
                    alpha=1.0 if win['is_impact'] else 0.15, marker='o' if win['is_active'] else 'x',
                    s=80 if win['is_active'] else 60, zorder=3, edgecolors='white' if win['is_impact'] else 'none')

    plt.title(f"Space Occupation Analysis | Player {momentum_data['player_id']}", color='white', fontsize=20)
    plt.xlim(momentum_data['tenure'][0] / (meta['fps'] * 60.0), momentum_data['tenure'][1] / (meta['fps'] * 60.0))
    plt.xlabel("Minutes", color='white'); plt.ylabel("Gain $G_i$", color='white'); plt.tick_params(colors='white')
    plt.tight_layout(); plt.show()

def plot_sg_momentum(sg_df, player_id, p_start_frame, p_end_frame, fps=50):
    """
    Render a momentum profile of Space Generation attraction intensity over match time.
    """
    if sg_df.empty: return
    sg_df['minutes'] = sg_df['frame'] / (fps * 60.0)
    plt.figure(figsize=(32, 6), facecolor='#1a1a1a')
    ax = plt.gca(); ax.set_facecolor('#1a1a1a')

    plt.vlines(sg_df['minutes'], 0, sg_df['attraction'] * -1, color='cyan', alpha=0.3, linewidth=1)
    scatter = plt.scatter(sg_df['minutes'], sg_df['attraction'] * -1, c=sg_df['attraction'] * -1, cmap='winter', s=100, edgecolor='white', zorder=3)
    plt.xlim(p_start_frame / (fps * 60.0), p_end_frame / (fps * 60.0))
    plt.title(f"Space Generation Momentum | Player {player_id}", color='white', fontsize=20)
    plt.xlabel("Match Time (Minutes)", color='white'); plt.ylabel("Attraction Intensity", color='white')
    plt.colorbar(scatter).set_label('Dragging Intensity', color='white')
    plt.tick_params(colors='white'); plt.show()

def plot_sg_event_analysis(event_idx, sg_df, w, team_df, opponent_df, df_ball, model, pc_grid, device, pc_calc_func):
    """
    Analyze specific Space Generation events, highlighting interaction between Generator and Receiver.
    """
    event = sg_df.iloc[event_idx]; t, gen_id, rec_id = int(event['frame']), int(event['generator_id']), int(event['receiver_id'])
    defenders = sg_df[(sg_df['frame'] == t) & (sg_df['generator_id'] == gen_id) & (sg_df['receiver_id'] == rec_id)]['defender_id'].unique().tolist()
    frames = [t, t + w]
    
    with torch.no_grad():
        V_batch = F.interpolate(model(torch.tensor(df_ball[df_ball['frame'].isin(frames)].set_index('frame')[['x', 'y']].values, dtype=torch.float32).to(device)).reshape(2, 1, 15, 21), 
                                size=pc_grid.shape[:2], mode='bilinear', align_corners=True).squeeze(1).cpu().numpy()

    pitch = Pitch(pitch_type='wyscout', line_color='#7c7c7c', goal_type='box', pitch_color='#1a1a1a')
    fig, axes = plt.subplots(1, 2, figsize=(22, 10), facecolor='#1a1a1a')

    for i, f in enumerate(frames):
        ax = axes[i]; pitch.draw(ax=ax); ax.set_aspect(68/105) 
        ball_pos = df_ball[df_ball['frame']==f].iloc[0][['x', 'y']].values
        pc_surf = pc_calc_func(team_df[team_df['frame'] == f], opponent_df[opponent_df['frame'] == f], ball_pos, pc_grid, sx=1.05, sy=0.68)
        ax.imshow(pc_surf * V_batch[i], extent=[0, 100, 0, 100], origin='lower', cmap='magma', alpha=0.8, zorder=0, aspect=0.64)

        for p_id, color, ring, lw in [(gen_id, '#FF4B4B', 'lime', 3), (rec_id, '#FF4B4B', 'yellow', 3)]:
            p = team_df[(team_df['frame'] == f) & (team_df['player'] == p_id)].iloc[0]
            pitch.scatter(p.x, p.y, s=80, c=color, edgecolors='white', ax=ax, zorder=14)
            pitch.scatter(p.x, p.y, s=500, facecolors='none', edgecolors=ring, linewidth=lw, ax=ax, zorder=15)

        for d_id in defenders:
            p_def = opponent_df[(opponent_df['frame'] == f) & (opponent_df['player'] == d_id)]
            if not p_def.empty:
                pitch.scatter(p_def.iloc[0].x, p_def.iloc[0].y, s=500, facecolors='none', edgecolors='white', linewidth=2, linestyle='--', ax=ax, zorder=15)

        pitch.scatter(ball_pos[0], ball_pos[1], s=60, c='white', edgecolors='black', ax=ax, zorder=20)
        ax.set_title(f"{'START' if i == 0 else 'END'} | Frame: {f}", color='white', fontsize=16)

    plt.suptitle(f"Space Generation Analysis | {gen_id} → {rec_id}", color='white', fontsize=22, y=0.98)
    plt.tight_layout(); plt.show()

def plot_sgg_heatmap_focused(matrix_df):
    """
    Synthesize a synergistic heatmap showing Space Generation Gain (SGG) across core attacking pairs.
    """
    plt.figure(figsize=(10, 8), facecolor='#1a1a1a')
    ax = sns.heatmap(matrix_df, annot=True, fmt=".0f", cmap='YlOrRd', linewidths=1.5, linecolor='#1a1a1a', cbar_kws={'label': 'Successful SGG Events'})
    plt.title("Attacking Core Synergy: Space Generation Gain", color='white', fontsize=18, pad=20)
    plt.xlabel("Receiver (The Beneficiary)", color='white'); plt.ylabel("Generator (The Decoy)", color='white')
    ax.tick_params(axis='x', colors='white'); ax.tick_params(axis='y', colors='white'); plt.show()