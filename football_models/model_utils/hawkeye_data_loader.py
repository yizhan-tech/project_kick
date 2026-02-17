import os
import re
from collections import defaultdict

import pandas as pd

def parse_file_sequence(filename):
    name_no_ext = os.path.splitext(filename)[0]
    
    match = re.search(r'_(\d+)_(\d+)(?:_(\d+))?_full$', name_no_ext)
    
    if match:
        half = int(match.group(1))
        minute = int(match.group(2))
        
        sub_minute = int(match.group(3)) if match.group(3) else 0
        
        return (half, minute, sub_minute)
    
    # Fallback for files that don't match the pattern
    return (99, 99, 99)

def process_tracking_files(sorted_files, data_dir, cols_to_keep):
    store = defaultdict(list)
    global_time_offset = 0.0
    
    print(f"Starting processing on {len(sorted_files)} files...")

    for i, filename in enumerate(sorted_files):
        file_path = os.path.join(data_dir, filename)
        
        # Get Half info from filename 
        current_half_id, _, _ = parse_file_sequence(filename)

        # Read the file
        try:
            df_chunk = pd.read_csv(file_path, low_memory=False)
        except Exception as e:
            print(f"Skipping {filename}: {e}")
            continue

        # Add Context Columns
        df_chunk['half'] = current_half_id

        # Filter Columns
        current_valid_cols = [c for c in cols_to_keep if c in df_chunk.columns]
        
        df_chunk = df_chunk[current_valid_cols + ['half']]
        
        # Drop rows where player is unidentifiable
        df_chunk = df_chunk.dropna(subset=['team_id', 'jersey_number'])

        # Continuous Time Logic
        if i > 0:
            df_chunk['time_key'] = df_chunk['time_key'] + global_time_offset
        
        current_max = df_chunk['time_key'].max()
        global_time_offset = current_max

        # Group by Stable Identifiers (Team + Jersey)
        groups = df_chunk.groupby(['team_id', 'jersey_number'])
        
        for (team_id, jersey_num), player_chunk in groups:
            store[(team_id, jersey_num)].append(player_chunk)

        # Progress Log
        if i % 10 == 0:
            print(f"Processed file {i}/{len(sorted_files)}: {filename[-15:]} | Half: {current_half_id} | Max Time: {global_time_offset:.2f}")

    print("Processing complete. Chunks stored in memory.")
    return store

def create_player_dataframes(player_data_store, team_id_map, player_name_map):
    player_dfs = {}
    missing_log = []

    print("Aggregating and naming players...")

    for (team_id, jersey_num), chunks in player_data_store.items():
        
        full_player_df = pd.concat(chunks, ignore_index=True)
        full_player_df = full_player_df.sort_values('time_key')
        
        clean_team_id = str(team_id).strip()
        team_name = team_id_map.get(clean_team_id, "UnknownTeam")
        
        # Resolve Player Name & Fix Jersey Number
        try:
            j_num_int = int(jersey_num)
        except (ValueError, TypeError):
            j_num_int = -1 
            
        player_name = player_name_map.get((team_name, j_num_int), "Unknown")
        
        full_player_df['team_name'] = team_name
        full_player_df['player_name'] = player_name
        full_player_df['jersey_number'] = j_num_int 
        
        # Log issues
        if player_name == "Unknown":
            missing_log.append(f"MISSING NAME: Team {team_name} | Jersey {j_num_int} (ID: {team_id})")

        if player_name != "Unknown":
            key_name = f"{team_name}_{player_name}"
        else:
            key_name = f"{team_name}_Jersey_{j_num_int}"
        
        player_dfs[key_name] = full_player_df

    print(f"Successfully created {len(player_dfs)} unique player profiles.")
    print("-" * 40)

    if missing_log:
        print(f"WARNING: {len(missing_log)} players were not found in your mapping:")
        for msg in missing_log[:5]: 
            print(msg)
        if len(missing_log) > 5:
            print(f"...and {len(missing_log)-5} more.")
    else:
        print("SUCCESS: All players were successfully assigned a name!")
        
    return player_dfs

def convert_and_separate(player_dfs, home_team_name, pitch_length=105.0, pitch_width=68.0):
    print(f"Standardizing and separating data for Home Team: {home_team_name}...")
    
    home_team_dfs = {}
    away_team_dfs = {}
    
    # Track metrics
    skipped_count = 0
    
    for key, df in player_dfs.items():
        
        # Determine Team Identity
        is_home_team = (df['team_name'].iloc[0] == home_team_name)
        
        # SAFETY CHECK 
        x_min, x_max = df['player_x'].min(), df['player_x'].max()
        y_min, y_max = df['player_y'].min(), df['player_y'].max()
        needs_conversion = not (x_min >= 0 and x_max <= 100 and y_min >= 0 and y_max <= 100)
        
        if not needs_conversion:
            skipped_count += 1
        else:
            df['player_x'] = ((df['player_x'] / pitch_length) + 0.5) * 100
            
            df['player_y'] = (1 - ((df['player_y'] / pitch_width) + 0.5)) * 100
            
            if is_home_team:
                flip_mask = (df['half'] == 2)
            else:
                flip_mask = (df['half'] == 1)
                
            df.loc[flip_mask, 'player_x'] = 100 - df.loc[flip_mask, 'player_x']
            df.loc[flip_mask, 'player_y'] = 100 - df.loc[flip_mask, 'player_y']
            
            df['player_x'] = df['player_x'].clip(0, 100)
            df['player_y'] = df['player_y'].clip(0, 100)

        if is_home_team:
            home_team_dfs[key] = df
        else:
            away_team_dfs[key] = df

    print(f"Process complete.")
    print(f"- Skipped conversion for {skipped_count} players (already Wyscout).")
    print(f"- Home Team Players: {len(home_team_dfs)}")
    print(f"- Away Team Players: {len(away_team_dfs)}")
    
    return home_team_dfs, away_team_dfs

def get_outfield_starters(home_team_dfs, limit=10):
    starters_info = []
    
    for key, df in home_team_dfs.items():
        # Check player role (exclude Goalkeepers)
        role = df['player_role'].iloc[0] if 'player_role' in df.columns else 'Unknown'
        
        # Check if they were on the pitch within the first 10 seconds
        is_starter = df['time_key'].min() < 10 
        
        if is_starter and 'Goalkeeper' not in role:
            starters_info.append({
                'key': key,
                'df': df,
                'jersey': df['jersey_number'].iloc[0]
            })
            
    starters_info.sort(key=lambda x: x['jersey'])
    
    return starters_info[:limit]

