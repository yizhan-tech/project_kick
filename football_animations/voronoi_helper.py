from manim import *
import pandas as pd
import numpy as np

class VoronoiManager:
    def __init__(self, scene, pitch, attack_color=ORANGE, defense_color=BLUE_E):
        self.scene = scene
        self.pitch = pitch
        self.attack_color = attack_color
        self.defense_color = defense_color
        self.alpha = 0.28
        
        self.player_dots = {} 
        self.polygons = []
        self.ball = None

    def get_frame_data(self, df, frame_id):
        """Extracts players and ball from a specific frame."""
        frame = df[df["frame"] == frame_id]
        players = [] 
        ball_pos = None

        for _, row in frame.iterrows():
            team = str(row.get("team", "")).strip().lower()
            pos = (row["x"], row["y"])
            
            if team in ["attack", "defense"]:
                color = self.attack_color if team == "attack" else self.defense_color
                players.append({
                    "pos": pos, 
                    "color": color, 
                    "id": row.get("player", -1), 
                    "team": team
                })
            else:
                ball_pos = pos
        return players, ball_pos

    def animate_intro_one_by_one(self, df, deploy_time=0.4, frame_id=0):
        """Adds players in a strict alternating team sequence."""
        players, _ = self.get_frame_data(df, frame_id)
        
        attackers = [p for p in players if p["team"] == "attack"]
        defenders = [p for p in players if p["team"] == "defense"]
        
        interleaved = []
        for d, a in zip(defenders, attackers):
            interleaved.append(d)
            interleaved.append(a)
            
        remaining = defenders[len(attackers):] + attackers[len(defenders):]
        interleaved.extend(remaining)

        current_sites = []
        current_colors = []

        self.polygons = VGroup()

        for p in interleaved:
            dot_pos = self.pitch.wyscout_to_manim(*p["pos"])
            dot = Dot(dot_pos, color=p["color"], radius=0.1, stroke_width=2, stroke_color=WHITE).set_z_index(10)
            
            current_sites.append(p["pos"])
            current_colors.append(p["color"])
            self.player_dots[(p["team"], p["id"])] = dot

            cells = self.pitch.get_voronoi_cells(current_sites)
            
            new_vgroup = VGroup(*[
                Polygon(*cell, fill_color=c, fill_opacity=self.alpha, stroke_width=1, stroke_color=WHITE).set_z_index(-1)
                for cell, c in zip(cells, current_colors) if len(cell) >= 3
            ])

            if len(self.polygons) == 0:
                self.polygons.become(new_vgroup)
                self.scene.play(FadeIn(dot), FadeIn(self.polygons), run_time=deploy_time)
            else:
                self.scene.play(
                    FadeIn(dot),
                    self.polygons.animate.become(new_vgroup),
                    run_time=0.4
                )

    def display_direct(self, df, frame_id=0):
        """Fades in all players and the full Voronoi diagram at once."""
        players, _ = self.get_frame_data(df, frame_id)
        sites = [p["pos"] for p in players]
        colors = [p["color"] for p in players]
        
        cells = self.pitch.get_voronoi_cells(sites)
        
        dots = []
        for p in players:
            dot_pos = self.pitch.wyscout_to_manim(*p["pos"])
            dot = Dot(dot_pos, color=p["color"], radius=0.1, stroke_width=2, stroke_color=WHITE).set_z_index(10)
            self.player_dots[(p["team"], p["id"])] = dot
            dots.append(dot)

        self.polygons = VGroup(*[
            Polygon(*cell, fill_color=c, fill_opacity=self.alpha, stroke_width=1, stroke_color=WHITE).set_z_index(-1)
            for cell, c in zip(cells, colors) if len(cell) >= 3
        ])

        self.scene.play(
            FadeIn(VGroup(*dots)),
            FadeIn(self.polygons),
            run_time=1
        )

    def run_animation(self, df, fps=25):
        """Loops through frames using .become() to prevent ghosting."""
        frames = sorted(df["frame"].unique())
        
        for f in frames:
            players, ball_pos = self.get_frame_data(df, f)
            sites = [p["pos"] for p in players]
            colors = [p["color"] for p in players]

            cells = self.pitch.get_voronoi_cells(sites)
            new_vgroup = VGroup(*[
                Polygon(
                    *cell, 
                    fill_color=c, 
                    fill_opacity=self.alpha, 
                    stroke_width=1, 
                    stroke_color=WHITE
                ).set_z_index(-1)
                for cell, c in zip(cells, colors) if len(cell) >= 3
            ])
            
            self.polygons.become(new_vgroup)

            for p in players:
                key = (p["team"], p["id"])
                new_pos = self.pitch.wyscout_to_manim(*p["pos"])
                if key in self.player_dots:
                    self.player_dots[key].move_to(new_pos)
                else:
                    dot = Dot(new_pos, color=p["color"], radius=0.1, stroke_width=2, stroke_color=WHITE).set_z_index(10)
                    self.player_dots[key] = dot
                    self.scene.add(dot)

            if ball_pos:
                b_pos = self.pitch.wyscout_to_manim(*ball_pos)
                if self.ball is None:
                    self.ball = Dot(b_pos, color=WHITE, radius=0.07).set_z_index(20)
                    self.scene.add(self.ball)
                else:
                    self.ball.move_to(b_pos)

            self.scene.wait(1/fps)