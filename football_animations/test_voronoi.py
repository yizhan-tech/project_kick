from pitch_utils import *
from voronoi_helper import *

class MyVoronoiScene(Scene):
    def construct(self):
        pitch = StandardPitch(scale=0.08).draw_base_pitch()
        self.add(pitch)
        
        df = pd.read_csv("/path/to/tracking_data.csv")
        vm = VoronoiManager(self, pitch)
        
        # Choice 1: Add one by one
        vm.animate_intro_one_by_one(df, frame_id=0)
        
        # Choice 2: Display direct (Uncomment to use)
        # vm.display_direct(df, frame_id=0)
        
        vm.run_animation(df)