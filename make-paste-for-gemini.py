import os
import pyperclip # pip install pyperclip

# Configuration
EXTENSIONS = {'.cs', '.csproj', '.sln', '.md', '.json'}
IGNORE_DIRS = {'bin', 'obj', '.git', '.vs', '.idea'}

def bundle_code():
    output = []
    root_dir = os.getcwd()
    
    output.append(f"Project Context for: {os.path.basename(root_dir)}\n")
    
    for root, dirs, files in os.walk(root_dir):
        # Modify dirs in-place to skip ignored directories
        dirs[:] = [d for d in dirs if d not in IGNORE_DIRS]
        
        for file in files:
            ext = os.path.splitext(file)[1]
            if ext in EXTENSIONS:
                file_path = os.path.join(root, file)
                rel_path = os.path.relpath(file_path, root_dir)
                
                try:
                    with open(file_path, 'r', encoding='utf-8') as f:
                        content = f.read()
                        output.append(f"\n--- START FILE: {rel_path} ---\n")
                        output.append(content)
                        output.append(f"\n--- END FILE: {rel_path} ---\n")
                except Exception as e:
                    print(f"Skipping {rel_path}: {e}")

    full_text = "".join(output)
    pyperclip.copy(full_text)
    print(f"Bundled {len(full_text)} characters from project to CLIPBOARD.")

if __name__ == "__main__":
    bundle_code()