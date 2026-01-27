from manim import *
import numpy as np

class StandardPitch(VGroup):
    def __init__(self, scale=0.08, orientation="horizontal", **kwargs):
        self.orientation = orientation
        self.scale = scale
        
        super().__init__(**kwargs)

        # FIFA Standard Dimensions (meters)
        length = 105
        width = 68
        penalty_area_depth = 16.5
        penalty_area_width = 40.32
        goal_area_depth = 5.5
        goal_area_width = 18.32
        penalty_spot_distance = 11
        center_circle_radius = 9.15
        
        # Helper to handle orientation mapping
        def p(x, y):
            if self.orientation == "vertical":
                return np.array([-y * scale, x * scale, 0])
            return np.array([x * scale, y * scale, 0])

        # Main Pitch Outline
        l_s, w_s = length * scale, width * scale
        self.pitch = Rectangle(
            width=l_s if orientation == "horizontal" else w_s,
            height=w_s if orientation == "horizontal" else l_s,
            stroke_color=WHITE, stroke_width=3
        )

        # Halfway Line
        if orientation == "horizontal":
            self.halfway = Line(p(0, -width/2), p(0, width/2), stroke_width=3)
        else:
            self.halfway = Line(p(-width/2, 0), p(width/2, 0), stroke_width=3)

        # Center Circle & Spot
        self.center_circle = Circle(radius=center_circle_radius * scale, color=WHITE, stroke_width=3)
        self.center_spot = Dot(ORIGIN, color=WHITE, radius=0.04)

        # Penalty Areas
        pa_w, pa_h = penalty_area_depth * scale, penalty_area_width * scale
        self.pa_left = Rectangle(
            width=pa_w if orientation == "horizontal" else pa_h,
            height=pa_h if orientation == "horizontal" else pa_w,
            stroke_width=3
        ).move_to(p(-length/2 + penalty_area_depth/2, 0))
        self.pa_right = self.pa_left.copy().move_to(p(length/2 - penalty_area_depth/2, 0))

        # Goal Areas
        ga_w, ga_h = goal_area_depth * scale, goal_area_width * scale
        self.ga_left = Rectangle(
            width=ga_w if orientation == "horizontal" else ga_h,
            height=ga_h if orientation == "horizontal" else ga_w,
            stroke_width=3
        ).move_to(p(-length/2 + goal_area_depth/2, 0))
        self.ga_right = self.ga_left.copy().move_to(p(length/2 - goal_area_depth/2, 0))

        # Penalty Spots
        self.ps_left = Dot(p(-length/2 + penalty_spot_distance, 0), radius=0.04)
        self.ps_right = Dot(p(length/2 - penalty_spot_distance, 0), radius=0.04)

        # Penalty Arcs (The 'D')
        arc_angle = 1.25 # Radians
        self.left_arc = Arc(
            radius=center_circle_radius * scale,
            start_angle=-arc_angle/2 if orientation == "horizontal" else PI/2 - arc_angle/2,
            angle=arc_angle, stroke_width=3
        ).move_to(p(-length/2 + penalty_spot_distance + 2.3, 0))

        self.right_arc = Arc(
            radius=center_circle_radius * scale,
            start_angle=PI - arc_angle/2 if orientation == "horizontal" else -PI/2 - arc_angle/2,
            angle=arc_angle, stroke_width=3
        ).move_to(p(length/2 - penalty_spot_distance - 2.3, 0))

        self.add(
            self.pitch, self.halfway, self.center_circle, self.center_spot,
            self.pa_left, self.pa_right, self.ga_left, self.ga_right,
            self.ps_left, self.ps_right, self.left_arc, self.right_arc
        )