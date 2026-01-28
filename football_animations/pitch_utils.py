from manim import *
import numpy as np

class StandardPitch(VGroup):
    def __init__(self, scale=0.08, orientation="horizontal", **kwargs):
        super().__init__(**kwargs)
        self.scale = scale
        self.orientation = orientation
        
        # FIFA Constants (Meters)
        self.dims = {
            "length": 105,
            "width": 68,
            "pa_depth": 16.5,
            "pa_width": 40.32,
            "ga_depth": 5.5,
            "ga_width": 18.32,
            "penalty_spot": 11,
            "center_circle": 9.15
        }

    # ---------------------------------------------------------
    # Coordinate Conversion
    # ---------------------------------------------------------
    def to_coord(self, x, y):
        """Maps raw meter coordinates to Manim vectors based on orientation."""
        if self.orientation == "vertical":
            # Vertical: Length is on Y-axis, Width is on X-axis (inverted for standard view)
            return np.array([-y * self.scale, x * self.scale, 0])
        # Horizontal: Length is on X-axis, Width is on Y-axis
        return np.array([x * self.scale, y * self.scale, 0])

    # ---------------------------------------------------------
    # Visual Drawing Methods
    # ---------------------------------------------------------
    def draw_base_pitch(self, stroke_color=WHITE, stroke_width=3):
        """Generates the basic lines and markings of a standard pitch."""
        d = self.dims
        l, w = d["length"], d["width"]
        
        # 1. Main Boundary & Halfway
        pitch_w = l * self.scale if self.orientation == "horizontal" else w * self.scale
        pitch_h = w * self.scale if self.orientation == "horizontal" else l * self.scale
        
        boundary = Rectangle(
            width=pitch_w, height=pitch_h, 
            stroke_color=stroke_color, stroke_width=stroke_width
        )

        halfway = Line(
            self.to_coord(0, -w/2), self.to_coord(0, w/2), 
            stroke_color=stroke_color, stroke_width=stroke_width
        ) if self.orientation == "horizontal" else Line(
            self.to_coord(-w/2, 0), self.to_coord(w/2, 0),
            stroke_color=stroke_color, stroke_width=stroke_width
        )

        # 2. Center Markings
        center_circle = Circle(radius=d["center_circle"] * self.scale, color=stroke_color, stroke_width=stroke_width)
        center_spot = Dot(ORIGIN, color=stroke_color, radius=0.04)

        # 3. Penalty Areas
        pa_w, pa_h = d["pa_depth"] * self.scale, d["pa_width"] * self.scale
        ga_w, ga_h = d["ga_depth"] * self.scale, d["ga_width"] * self.scale

        pa_l = Rectangle(
            width=pa_w if self.orientation == "horizontal" else pa_h,
            height=pa_h if self.orientation == "horizontal" else pa_w,
            stroke_width=stroke_width
        ).move_to(self.to_coord(-l/2 + d["pa_depth"]/2, 0))

        ga_l = Rectangle(
            width=ga_w if self.orientation == "horizontal" else ga_h,
            height=ga_h if self.orientation == "horizontal" else ga_w,
            stroke_width=stroke_width
        ).move_to(self.to_coord(-l/2 + d["ga_depth"]/2, 0))

        pa_r = pa_l.copy().move_to(self.to_coord(l/2 - d["pa_depth"]/2, 0))
        ga_r = ga_l.copy().move_to(self.to_coord(l/2 - d["ga_depth"]/2, 0))

        # 4. Spots & Arcs
        ps_l = Dot(self.to_coord(-l/2 + d["penalty_spot"], 0), radius=0.04)
        ps_r = Dot(self.to_coord(l/2 - d["penalty_spot"], 0), radius=0.04)

        arc_angle = 1.8 
        arc_rad = d["center_circle"] * self.scale
        
        penalty_arc_l = Arc(
            radius=arc_rad,
            start_angle=-arc_angle/2 if self.orientation == "horizontal" else PI/2 - arc_angle/2,
            angle=arc_angle, stroke_width=stroke_width
        ).shift(ps_l.get_center())

        penalty_arc_r = Arc(
            radius=arc_rad,
            start_angle=PI - arc_angle/2 if self.orientation == "horizontal" else -PI/2 - arc_angle/2,
            angle=arc_angle, stroke_width=stroke_width
        ).shift(ps_r.get_center())

        # Add everything to this VGroup
        self.add(
            boundary, halfway, center_circle, center_spot,
            pa_l, pa_r, ga_l, ga_r, ps_l, ps_r, penalty_arc_l, penalty_arc_r
        )
        return self
    
    def add_spatial_pattern(self):
        pass

    def plot_tracking_frame(self):
        pass

    def add_influence_field(self):
        pass