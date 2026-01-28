from manim import *
import numpy as np

class StandardPitch(VGroup):
    def __init__(self, scale=0.08, orientation="horizontal", **kwargs):
        self.orientation = orientation
        self.scale = scale
        super().__init__(**kwargs)

        # ---------------------------------------------------------
        # 1. FIFA Standard Dimensions (Meters)
        # ---------------------------------------------------------
        FIELD_LENGTH = 105
        FIELD_WIDTH = 68
        
        PENALTY_AREA_DEPTH = 16.5
        PENALTY_AREA_WIDTH = 40.32
        
        GOAL_AREA_DEPTH = 5.5
        GOAL_AREA_WIDTH = 18.32
        
        PENALTY_SPOT_DIST = 11
        CENTER_CIRCLE_RADIUS = 9.15
        
        # ---------------------------------------------------------
        # 2. Coordinate Helper
        # ---------------------------------------------------------
        def to_coord(x, y):
            """Maps raw meter coordinates to Manim vectors based on orientation."""
            if self.orientation == "vertical":
                return np.array([-y * self.scale, x * self.scale, 0])
            return np.array([x * self.scale, y * self.scale, 0])

        # ---------------------------------------------------------
        # 3. Main Pitch Geometry
        # ---------------------------------------------------------
        pitch_w = FIELD_LENGTH * self.scale if orientation == "horizontal" else FIELD_WIDTH * self.scale
        pitch_h = FIELD_WIDTH * self.scale if orientation == "horizontal" else FIELD_LENGTH * self.scale
        
        self.boundary = Rectangle(
            width=pitch_w, height=pitch_h, 
            stroke_color=WHITE, stroke_width=3
        )

        if orientation == "horizontal":
            self.halfway_line = Line(to_coord(0, -FIELD_WIDTH/2), to_coord(0, FIELD_WIDTH/2), stroke_width=3)
        else:
            self.halfway_line = Line(to_coord(-FIELD_WIDTH/2, 0), to_coord(FIELD_WIDTH/2, 0), stroke_width=3)

        self.center_circle = Circle(radius=CENTER_CIRCLE_RADIUS * self.scale, color=WHITE, stroke_width=3)
        self.center_spot = Dot(ORIGIN, color=WHITE, radius=0.04)

        # ---------------------------------------------------------
        # 4. Penalty & Goal Areas
        # ---------------------------------------------------------
        pa_rect_w = PENALTY_AREA_DEPTH * self.scale
        pa_rect_h = PENALTY_AREA_WIDTH * self.scale
        ga_rect_w = GOAL_AREA_DEPTH * self.scale
        ga_rect_h = GOAL_AREA_WIDTH * self.scale

        self.penalty_area_left = Rectangle(
            width=pa_rect_w if orientation == "horizontal" else pa_rect_h,
            height=pa_rect_h if orientation == "horizontal" else pa_rect_w,
            stroke_width=3
        ).move_to(to_coord(-FIELD_LENGTH/2 + PENALTY_AREA_DEPTH/2, 0))

        self.goal_area_left = Rectangle(
            width=ga_rect_w if orientation == "horizontal" else ga_rect_h,
            height=ga_rect_h if orientation == "horizontal" else ga_rect_w,
            stroke_width=3
        ).move_to(to_coord(-FIELD_LENGTH/2 + GOAL_AREA_DEPTH/2, 0))

        self.penalty_area_right = self.penalty_area_left.copy().move_to(to_coord(FIELD_LENGTH/2 - PENALTY_AREA_DEPTH/2, 0))
        self.goal_area_right = self.goal_area_left.copy().move_to(to_coord(FIELD_LENGTH/2 - GOAL_AREA_DEPTH/2, 0))

        self.penalty_spot_left = Dot(to_coord(-FIELD_LENGTH/2 + PENALTY_SPOT_DIST, 0), radius=0.04)
        self.penalty_spot_right = Dot(to_coord(FIELD_LENGTH/2 - PENALTY_SPOT_DIST, 0), radius=0.04)

        # ---------------------------------------------------------
        # 5. Penalty Arcs 
        # ---------------------------------------------------------
        ARC_ANGLE = 1.8  # Radians
        arc_rad = CENTER_CIRCLE_RADIUS * self.scale
        
        self.penalty_arc_left = Arc(
            radius=arc_rad,
            start_angle=-ARC_ANGLE/2 if orientation == "horizontal" else PI/2 - ARC_ANGLE/2,
            angle=ARC_ANGLE, stroke_width=3
        ).shift(self.penalty_spot_left.get_center())

        self.penalty_arc_right = Arc(
            radius=arc_rad,
            start_angle=PI - ARC_ANGLE/2 if orientation == "horizontal" else -PI/2 - ARC_ANGLE/2,
            angle=ARC_ANGLE, stroke_width=3
        ).shift(self.penalty_spot_right.get_center())

        # ---------------------------------------------------------
        # 6. Assembly
        # ---------------------------------------------------------
        self.add(
            self.boundary,
            self.halfway_line,
            self.center_circle,
            self.center_spot,
            self.penalty_area_left,
            self.penalty_area_right,
            self.goal_area_left,
            self.goal_area_right,
            self.penalty_spot_left,
            self.penalty_spot_right,
            self.penalty_arc_left,
            self.penalty_arc_right
        )