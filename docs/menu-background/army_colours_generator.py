import sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import numpy as np
ARMIES = [
 ("Alien Hives","ALIEN","#9C6BFF"), ("Battle Brothers","BAT-BR","#4F6BFF"), ("Blessed Sisters","B-SIST","#FF4D6D"),
 ("Custodian Brothers","C-BR","#F2E4BC"), ("DAO Union","DAO","#FF9F40"), ("Dark Elf Raiders","D-ELF","#E04DFF"), ("Dwarf Guilds","D-GUILD","#D09050"),
 ("Elven Jesters","ELF-J","#FF85C8"), ("Eternal Dynasty","E-DYN","#FFEB3B"), ("Goblin Reclaimers","GOB","#A6F58C"),
 ("Havoc Brothers","HAVOC","#E8352E"), ("High Elf Fleets","H-ELF","#8EC9FF"), ("Human Defense Force","HDF","#8CC63F"),
 ("Human Inquisition","H-INQ","#E6EAF2"), ("Infected Colonies","INFECT","#D4E157"), ("Jackals","JAK","#D8A878"),
 ("Machine Cults","M-CULT","#E0612A"), ("Orc Marauders","ORK","#3DDC5A"), ("Ratmen Clans","RAT","#FFB000"),
 ("Rebel Guerrillas","REB-G","#B5A66A"), ("Robot Legions","ROBOT","#8C4DFF"), ("Saurian Starhost","S-STAR","#2EE6A6"),
 ("Soul-Snatcher Cults","S-CULT","#C58CFF"), ("Titan Lords","TITAN","#FFD447"), ("Wormhole Daemons","DEMON","#FF2E88"),
]
W, H, SS = 1920, 1080, 2
im = Image.new("RGB", (W*SS, H*SS), (3, 5, 10)); d = ImageDraw.Draw(im)
for x in range(0, W, 30): d.line([(x*SS,0),(x*SS,H*SS)], fill=(8,26,44), width=SS)
for y in range(0, H, 30): d.line([(0,y*SS),(W*SS,y*SS)], fill=(8,26,44), width=SS)
mono = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf", 22*SS)
small = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 17*SS)
hdr = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 26*SS)
d.text((40*SS, 24*SS), "Proposed army colours (cyan box = terrain colour for contrast)", font=hdr, fill=(220,240,255))
cols, cw, rh, x0, y0 = 3, 620, 104, 40, 80
for i, (name, ab, hx) in enumerate(ARMIES):
    c = tuple(int(hx[k:k+2], 16) for k in (1,3,5))
    cx, cy = x0 + (i % cols)*cw, y0 + (i // cols)*rh
    # cyan terrain reference box
    d.rectangle([((cx+520)*SS,(cy+22)*SS),((cx+580)*SS,(cy+62)*SS)], outline=(50,184,255), width=2*SS)
    # bases + flag
    for j in range(4):
        bx = cx + 30 + j*34
        d.ellipse([(bx-13)*SS,(cy+60)*SS,(bx+13)*SS,(cy+80)*SS], outline=c, width=3*SS)
    fx = cx + 64
    d.line([(fx*SS,(cy+70)*SS),(fx*SS,(cy+14)*SS)], fill=c, width=3*SS)
    d.polygon([(fx*SS,(cy+14)*SS),((fx+28)*SS,(cy+22)*SS),(fx*SS,(cy+30)*SS)], fill=tuple(int(v*0.55) for v in c), outline=c)
    d.text(((cx+180)*SS,(cy+14)*SS), f"{ab} 1", font=mono, fill=c)
    d.text(((cx+180)*SS,(cy+48)*SS), f"{name}", font=small, fill=(200,215,230))
    d.text(((cx+180)*SS,(cy+72)*SS), hx, font=small, fill=(120,140,160))
im = im.resize((W, H), Image.LANCZOS)
a = np.asarray(im, float)
g = np.asarray(im.filter(ImageFilter.GaussianBlur(5)), float)
Image.fromarray(np.clip(a + g*0.7, 0, 255).astype(np.uint8)).save(sys.argv[1]); print("ok")
