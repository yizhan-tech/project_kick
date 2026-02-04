from manim import *
import numpy as np
from pitch_utils import StandardPitch

# ====================================================
# 1. HELPER CLASS: MetricSurface
# ====================================================
class MetricSurface(VGroup):
    def __init__(
        self, 
        pitch, 
        matrix, 
        max_height=3.5, 
        min_color=ManimColor("#0077B6"), 
        max_color=ManimColor("#00FFFF"), 
        stroke_color=BLACK,
        stroke_width=0.1, 
        fill_opacity=0.9,
        **kwargs
    ):
        super().__init__(**kwargs)
        self.pitch = pitch
        self.matrix = np.array(matrix)
        self.max_height = max_height
        self.min_color = min_color
        self.max_color = max_color
        self.stroke_color = stroke_color
        self.stroke_width = stroke_width
        self.fill_opacity = fill_opacity
        
        self._build_bars()

    def _build_bars(self):
        rows, cols = self.matrix.shape
        
        if hasattr(self.pitch, "field_width"):
            p_width = self.pitch.field_width
            p_height = self.pitch.field_height
        elif hasattr(self.pitch, "dims") and hasattr(self.pitch, "scale"):
            orientation = getattr(self.pitch, "orientation", "horizontal")
            length_m = self.pitch.dims["length"] * self.pitch.scale
            width_m = self.pitch.dims["width"] * self.pitch.scale
            if orientation == "horizontal":
                p_width = length_m
                p_height = width_m
            else:
                p_width = width_m
                p_height = length_m
        else:
            p_width = self.pitch.width
            p_height = self.pitch.height

        cell_w = p_width / cols
        cell_h = p_height / rows
        
        # Normalize matrix
        mat_max = np.max(self.matrix)
        mat_min = np.min(self.matrix)
        norm_matrix = (self.matrix - mat_min) / (mat_max - mat_min) if mat_max > mat_min else np.zeros_like(self.matrix)

        for r in range(rows):
            for c in range(cols):
                val = self.matrix[r, c]
                norm_val = norm_matrix[r, c]
                
                h = (norm_val * self.max_height) + 0.01
                bar_color = interpolate_color(self.min_color, self.max_color, norm_val)

                bar = Prism(dimensions=[cell_w, cell_h, h])
                bar.set_fill(bar_color, opacity=self.fill_opacity)
                
                if self.stroke_width > 0:
                    bar.set_stroke(color=self.stroke_color, width=self.stroke_width)
                else:
                    bar.set_stroke(width=0)

                bar.set_shade_in_3d(True)

                x = (c - cols/2 + 0.5) * cell_w
                y = ((rows - 1 - r) - rows/2 + 0.5) * cell_h
                z = h / 2

                bar.move_to([x, y, z])
                self.add(bar)

    def get_growth_animation(self, run_time=2.5, lag_ratio=0.0, direction="bottom_up"):
        """
        Returns the LaggedStart animation for growing the bars.
        
        Args:
            run_time (float): Total duration of the animation.
            lag_ratio (float): 0.0 for simultaneous, >0.0 for wave effect.
            direction (str): 'bottom_up' (near to far) or 'top_down' (far to near).
        """
        # Sort based on Y position (screen depth)
        if direction == "bottom_up":
            # Sort by Y ascending (Screen Bottom -> Top)
            sorted_bars = sorted(self, key=lambda m: m.get_y())
        elif direction == "top_down":
            sorted_bars = sorted(self, key=lambda m: -m.get_y())
        else:
            sorted_bars = self

        return LaggedStart(
            *[
                GrowFromPoint(
                    bar, 
                    point=np.array([bar.get_x(), bar.get_y(), 0])
                ) 
                for bar in sorted_bars
            ],
            lag_ratio=lag_ratio,
            run_time=run_time
        )