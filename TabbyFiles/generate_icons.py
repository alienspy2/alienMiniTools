import os

ICONS_DIR = "src/resources/icons"

# Pastel Palette
# Yellow: #FFF176, Green: #81C784, Blue: #64B5F6, Purple: #9575CD, Orange: #FFB74D, Pink: #F06292

icons = {
    "app_icon.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect x="10" y="10" width="44" height="44" rx="12" fill="#81C784"/><circle cx="24" cy="28" r="4" fill="#1B5E20"/><circle cx="40" cy="28" r="4" fill="#1B5E20"/><path d="M24 40 Q32 46 40 40" stroke="#1B5E20" stroke-width="3" stroke-linecap="round" fill="none"/></svg>""",
    
    "folder.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z" fill="#FFF176"/><path d="M22 8v10c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V8h20z" fill="#FFEE58"/></svg>""",
    
    "up.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="#E0E0E0"/><path d="M8 14l4-4 4 4" stroke="#616161" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" fill="none"/></svg>""",
    
    "add.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="#81C784"/><path d="M12 7v10M7 12h10" stroke="white" stroke-width="2" stroke-linecap="round"/></svg>""",
    
    "file.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6z" fill="#64B5F6"/><path d="M13 3.5V9h5.5" fill="#42A5F5"/></svg>""",
    
    "tabby.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect x="2" y="4" width="20" height="16" rx="4" fill="#455A64"/><path d="M6 10l4 2-4 2" stroke="#B0BEC5" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" fill="none"/><line x1="12" y1="14" x2="18" y2="14" stroke="#B0BEC5" stroke-width="2" stroke-linecap="round"/></svg>""",
    
    "nemo.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="#FFB74D"/><path d="M12 4l3 8 7 2-7 2-3 8-3-8-7-2 7-2z" fill="#FFE0B2"/></svg>""",
    
    "antigravity.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="#9575CD"/><path d="M11 7h2v10h-2zM7 11h10v2H7z" fill="#D1C4E9"/></svg>""",
    
    "gemma.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><linearGradient id="g" x1="0%" y1="0%" x2="100%" y2="100%"><stop offset="0%" style="stop-color:#4FC3F7"/><stop offset="100%" style="stop-color:#BA68C8"/></linearGradient></defs><circle cx="12" cy="12" r="10" fill="url(#g)"/><path d="M9 10a2 2 0 1 1 0 4 2 2 0 0 1 0-4zm6 0a2 2 0 1 1 0 4 2 2 0 0 1 0-4z" fill="white"/></svg>""",

    "xed.svg": """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect x="4" y="2" width="16" height="20" rx="2" fill="#8D6E63"/><path d="M6 4h12v16H6z" fill="#D7CCC8"/><path d="M8 6h8v2H8zm0 4h8v2H8zm0 4h5v2H8z" fill="#8D6E63"/></svg>"""
}

if not os.path.exists(ICONS_DIR):
    os.makedirs(ICONS_DIR)

for name, content in icons.items():
    with open(os.path.join(ICONS_DIR, name), "w") as f:
        f.write(content)
        print(f"Generated cute pastel icon: {name}")
