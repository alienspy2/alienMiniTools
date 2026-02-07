
import os
import codecs

def add_bom(root_dir):
    start_dir = os.path.abspath(root_dir)
    print(f"Scanning {start_dir}")
    
    count = 0
    for root, dirs, files in os.walk(start_dir):
        for file in files:
            ext = os.path.splitext(file)[1].lower()
            if ext in {'.py', '.cs'}:
                path = os.path.join(root, file)
                try:
                    with open(path, 'rb') as f:
                        content = f.read()
                    
                    if not content.startswith(codecs.BOM_UTF8):
                        print(f"Adding BOM to {path}")
                        with open(path, 'wb') as f:
                            f.write(codecs.BOM_UTF8 + content)
                        count += 1
                except Exception as e:
                    print(f"Error processing {path}: {e}")
    print(f"Updated {count} files.")

if __name__ == "__main__":
    add_bom('.')
