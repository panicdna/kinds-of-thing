"""Original seated stick-figure routine: Korean poster and GIF preview."""
from pathlib import Path
from functools import lru_cache
import math
import json
from PIL import Image, ImageDraw, ImageFont, ImageOps, ImageSequence

ROOT = Path(__file__).resolve().parent
BG = '#f5f2ea'
INK = '#263a42'
MUTED = '#60727a'
TEAL = '#197d78'
CORAL = '#d86752'
PASTELS = ['#e4f1eb', '#e8eef8', '#f9ebdf', '#e4f1eb', '#e8eef8', '#f9ebdf']
REG = '/mnt/c/Windows/Fonts/malgun.ttf'
BOLD = '/mnt/c/Windows/Fonts/malgunbd.ttf'
STEPS = [
    {'name':'어깨 으쓱, 편하게 내리기', 'time':'3~5초 × 3회', 'lines':['어깨를 귀 쪽으로 올렸다가 내려요.', '목은 길게, 숨은 편하게.'], 'labels':['준비', '올리기 → 내리기']},
    {'name':'목을 좌우로 살짝', 'time':'좌우 각각 10초', 'lines':['귀를 어깨 쪽으로 조금 기울여요.', '어깨는 그대로, 손으로 당기지 않아요.'], 'labels':['가운데', '왼쪽 · 오른쪽']},
    {'name':'가슴 활짝 펴기', 'time':'10초 × 2회', 'lines':['팔꿈치를 뒤로 보내 어깨뼈를 모아요.', '허리를 젖히지 말고 가슴만 편하게.'], 'labels':['옆에서 본 준비', '팔꿈치를 뒤로']},
    {'name':'앞으로 뻗어 등 늘리기', 'time':'10~20초 유지', 'lines':['양손을 모아 앞으로 뻗어요.', '등은 부드럽게 둥글게, 고개는 살짝만.'], 'labels':['옆에서 본 준비', '앞으로 쭉']},
    {'name':'좌우 옆구리 늘리기', 'time':'좌우 각각 10초', 'lines':['두 팔을 올려 몸통을 조금 기울여요.', '양쪽 엉덩이는 의자에 붙여요.'], 'labels':['팔 올리고 준비', '왼쪽 · 오른쪽']},
    {'name':'손목 위아래 늘리기', 'time':'각 3~5초 · 양손', 'lines':['반대 손으로 손가락 쪽을 살짝 잡아요.', '손목을 위아래로, 세게 당기지 않아요.'], 'labels':['손목 위로 살짝', '손목 아래로 살짝']},
]

@lru_cache(maxsize=100)
def font(size, bold=False):
    return ImageFont.truetype(BOLD if bold else REG, int(size))


def text(draw, xy, value, size, color=INK, bold=False, anchor=None):
    draw.text(xy, value, font=font(size,bold), fill=color, anchor=anchor, stroke_width=0)


def line(draw, points, color, width):
    width=max(1,round(width))
    draw.line(points, fill=color, width=width, joint='curve')
    r=width/2
    for x,y in [points[0], points[-1]]:
        draw.ellipse((x-r,y-r,x+r,y+r),fill=color)


def arrow(draw, points, color=CORAL, width=5, head=11):
    line(draw,points,color,width)
    x,y=points[-1]; px,py=points[-2]
    a=math.atan2(y-py,x-px)
    draw.polygon([(x,y),(x-head*math.cos(a-.6),y-head*math.sin(a-.6)),(x-head*math.cos(a+.6),y-head*math.sin(a+.6))], fill=color)


def bezier(p0,p1,p2,p3):
    result=[]
    for j in range(31):
        t=j/30; q=1-t
        result.append((q**3*p0[0]+3*q*q*t*p1[0]+3*q*t*t*p2[0]+t**3*p3[0], q**3*p0[1]+3*q*q*t*p1[1]+3*q*t*t*p2[1]+t**3*p3[1]))
    return result


def figure(draw, x, y, s, step, amount, arrows=False, desk=True):
    """Feet and pelvis stay fixed; only neck/upper limbs/trunk move."""
    side=step in [2,3,5]
    def pt(p): return (x+s*p[0], y+s*p[1])
    def poly(ps,c=INK,w=5): line(draw,[pt(p) for p in ps],c,w*s)
    def box(coords,fill,outline=None,width=1,r=0):
        a,b,c,d=coords
        draw.rounded_rectangle((x+a*s,y+b*s,x+c*s,y+d*s),radius=r*s,fill=fill,outline=outline,width=max(1,round(width*s)))
    def circle(center,r,fill,outline=INK,w=4):
        cx,cy=pt(center)
        draw.ellipse((cx-r*s,cy-r*s,cx+r*s,cy+r*s),fill=fill,outline=outline,width=max(1,round(w*s)))
    def arr(ps): arrow(draw,[pt(p) for p in ps],CORAL,max(2,round(3*s)),8*s)
    # Context is secondary: a stable four-legged chair, a desk, and a monitor.
    poly([(-108,127),(235,127)],'#c8d5d2',2)
    if desk:
        box((142,-59,232,-51),'#c1cece',r=3)
        poly([(150,-49),(150,123)],'#a5b8b9',3)
        poly([(225,-49),(225,123)],'#a5b8b9',3)
        box((170,-132,220,-94),'#f8faf8','#a5b8b9',2,4)
        poly([(195,-93),(195,-60)],'#a5b8b9',3)
        poly([(178,-61),(211,-61)],'#a5b8b9',3)
    if side:
        box((-83,-90,-73,7),'#d1e1dc',r=4)
        box((-81,4,14,14),'#b9cfc8',r=4)
        poly([(-76,15),(-76,124)],'#8ea9a2',4)
        poly([(7,15),(7,124)],'#8ea9a2',4)
        poly([(-22,3),(34,17),(34,118),(57,118)],'#8c9a9f',4)
        poly([(-28,2),(47,12),(47,122),(72,122)],INK,5)
        hip=(-28,0); shoulder=(-28,-112)
        if step==2:
            shoulder=(-28-7*amount,-112)
            elbows=[(-3,-60),(-12,-58)]
            wrists=[(24,-7),(16,-4)]
            for j in range(2):
                e=(-3-82*amount+j*10,-60-21*amount)
                w=(24-80*amount+j*8,-7-37*amount)
                poly([shoulder,e,w],INK if j else '#829399',5 if j else 4)
                if amount>.6 and j:
                    circle(e,4,TEAL,TEAL,1)
            poly([hip,(-30,-54),shoulder],INK,6)
            neck=(shoulder[0],-137)
            head=(shoulder[0],-168)
            if arrows and amount>.5: arr([(-8,-93),(-75,-93)])
        elif step==3:
            shoulder=(-28+35*amount,-112+8*amount)
            poly(bezier(hip,(-28-26*amount,-36),(-28-20*amount,-95),shoulder),INK,6)
            neck=(shoulder[0]+3*amount,shoulder[1]-24)
            head=(neck[0]+6*amount,neck[1]-30)
            for j in [0,1]:
                e=(-1+58*amount,-65-32*amount+j*6)
                w=(28+75*amount,-10-87*amount+j*3)
                poly([shoulder,e,w],INK if j else '#829399',5 if j else 4)
            if amount>.5: circle((104,-98),4,TEAL,TEAL,1)
            if arrows and amount>.5: arr([(83,-123),(127,-123)])
        else:
            poly([hip,shoulder],INK,6)
            neck=(-28,-137); head=(-28,-168)
            wrist=(90,-107)
            fingertips=(114,-107-27*amount)
            poly([shoulder,(20,-107),wrist,fingertips],INK,5)
            # Small palm and fingers plus the other hand holding, not yanking.
            poly([(88,-103),(109,-103-24*amount)],INK,3)
            poly([shoulder,(10,-55),(108,-107-20*amount)],'#526d76',4)
            circle((108,-107-20*amount),4,'#526d76','#526d76',1)
            if arrows: arr([(127,-126),(135,-111),(127,-90)])
        poly([shoulder,neck],INK,5)
        circle(head,29,'#fffdf7',INK,4)
        circle((head[0]+10,head[1]-4),1.7,INK,INK,1)
        poly([(head[0]+20,head[1]+4),(head[0]+25,head[1]+6)],INK,2)
    else:
        box((-78,-88,78,8),'#e0eae5','#b1c7bf',2,8)
        box((-80,4,80,14),'#b9cfc8',r=4)
        for xx in [-70,70]: poly([(xx,15),(xx,125)],'#8ea9a2',4)
        for sign in [-1,1]:
            poly([(0,0),(sign*48,26),(sign*48,120),(sign*69,120)],INK,5)
        lean=.21*amount if step==4 else 0
        def rot(p): return (p[0]*math.cos(lean)-p[1]*math.sin(lean),p[0]*math.sin(lean)+p[1]*math.cos(lean))
        shifty=-19*amount if step==0 else 0
        shoulders=[(-28,-110+shifty),(28,-110+shifty)]
        center=(0,-110+shifty/2)
        if step==1:
            angle=.34*amount
            neck=(math.sin(angle)*27,-110-math.cos(angle)*27)
            poly([(0,0),center,neck],INK,6)
        else:
            poly([rot((0,0)),rot(center),rot((0,-137))],INK,6)
        if step==4:
            arms=[((-28,-110),(-48,-164),(-18,-218)),((28,-110),(48,-164),(18,-218))]
            for arm in arms: poly([rot(p) for p in arm],INK,5)
            poly([rot((-18,-218)),rot((18,-218))],INK,4)
        else:
            for sign,sh in zip([-1,1],shoulders):
                poly([sh,(sign*43,-57+shifty),(sign*35,-9+shifty)],INK,5)
            poly(shoulders,INK,5)
        neck=rot((0,-137))
        if step==1:
            angle=.34*amount
            hc=(math.sin(angle)*58,-110-math.cos(angle)*58)
        else:
            angle=lean; hc=rot((0,-168))
        circle(hc,29,'#fffdf7',INK,4)
        # Tiny face makes neck tilt legible, while retaining stick-figure simplicity.
        for ex in [-8,8]:
            ep=(hc[0]+ex*math.cos(angle)+3*math.sin(angle),hc[1]+ex*math.sin(angle)-3*math.cos(angle))
            circle(ep,1.5,INK,INK,1)
        smile=[]
        for i in range(11):
            xx=-7+14*i/10; yy=8+2*(1-(xx/7)**2)
            smile.append((hc[0]+xx*math.cos(angle)-yy*math.sin(angle),hc[1]+xx*math.sin(angle)+yy*math.cos(angle)))
        poly(smile,INK,1.6)
        if arrows and step==0:
            for sign in [-1,1]: arr([(sign*66,-87),(sign*66,-119)])
        if arrows and step==1: arr([(-29,-205),(0,-215),(42,-198)])
        if arrows and step==4: arr([(-56,-236),(0,-249),(72,-230)])
    circle((-28,0) if side else (0,0),4,TEAL,TEAL,1)


def wrist_detail(draw, x, y, s, amount):
    """Enlarged wrist and gently assisting hand; up/down share geometry."""
    radius=85*s
    draw.ellipse((x-radius,y-radius,x+radius,y+radius),fill='#fffdf8',outline='#a7bbb5',width=max(1,round(2*s)))
    sign=1 if amount>=0 else -1
    def p(xx,yy): return (x+xx*s,y+yy*sign*s)
    hand=[p(-67,5),p(-7,5),p(2,-7),p(6,-40),p(17,-45),p(28,-39),p(27,-9),p(9,25),p(-67,25)]
    draw.polygon(hand,fill='#fff4df')
    line(draw,hand+[hand[0]],INK,4*s)
    for xx in [12,18,23]: line(draw,[p(xx,-35),p(xx,-17)],'#9a7a62',1.2*s)
    # Blue hand is the opposite hand, resting across the fingers.
    line(draw,[p(62,48),p(49,16),p(31,-16),p(14,-27)],TEAL,10*s)
    line(draw,[p(14,-27),p(8,-19),p(18,-13)],TEAL,5*s)
    arrow(draw,[p(-19,-37),p(-8,-50),p(8,-55)],CORAL,2.5*s,7*s)


def poster():
    im=Image.new('RGB',(1800,2110),BG); d=ImageDraw.Draw(im)
    text(d,(60,40),'DESK BREAK  /  SEATED STRETCH',25,TEAL,True)
    text(d,(58,90),'앉아서 따라 하는 6가지 스트레칭',62,INK,True)
    text(d,(62,180),'양발은 바닥에 · 엉덩이는 의자에 · 반동 없이 천천히',30,MUTED)
    for n,step in enumerate(STEPS):
        cx=50+(n%2)*865; cy=260+(n//2)*548
        d.rounded_rectangle((cx+3,cy+7,cx+838,cy+529),radius=27,fill='#e4e2d9')
        d.rounded_rectangle((cx,cy,cx+835,cy+522),radius=27,fill=PASTELS[n])
        d.ellipse((cx+25,cy+25,cx+78,cy+78),fill=TEAL)
        text(d,(cx+51,cy+51),str(n+1),29,'#ffffff',True,'mm')
        text(d,(cx+96,cy+34),step['name'],32,INK,True)
        text(d,(cx+98,cy+83),step['time'],24,TEAL,True)
        # Two poses per panel: start and target. Wrist uses up/down targets.
        left_amount=1 if n==5 else 0
        right_amount=-1 if n==5 else 1
        if n==5:
            figure(d,cx+152,cy+310,.68,n,0,arrows=False)
            wrist_detail(d,cx+458,cy+250,1,1)
            wrist_detail(d,cx+696,cy+250,1,-1)
            text(d,(cx+160,cy+422),'앉아서 팔을 앞으로',20,MUTED,False,'mm')
            for px,label in [(cx+458,step['labels'][0]),(cx+696,step['labels'][1])]:
                text(d,(px,cy+365),label,21,INK,True,'mm')
            text(d,(cx+575,cy+409),'파란 손 = 반대 손으로 가볍게 잡기',20,TEAL,False,'mm')
        else:
            for px,amount in [(cx+196,left_amount),(cx+580,right_amount)]:
                figure(d,px,cy+310,.72,n,amount,arrows=(px>cx+400))
            arrow(d,[(cx+388,cy+245),(cx+436,cy+245)],TEAL,5,13)
            for px,label in [(cx+200,step['labels'][0]),(cx+601,step['labels'][1])]:
                text(d,(px,cy+422),label,21,MUTED,False,'mm')
        text(d,(cx+32,cy+455),step['lines'][0],24,INK)
        text(d,(cx+32,cy+490),step['lines'][1],22,MUTED)
    d.rounded_rectangle((50,1930,1750,2020),radius=20,fill='#f5ded4')
    text(d,(80,1947),'주의  통증·저림·어지럼이 생기면 즉시 중단하세요.',29,'#873e30',True)
    text(d,(80,1988),'최근 부상·수술이 있거나 치료 중이면 먼저 의료진에게 확인하세요.',23,'#873e30')
    text(d,(60,2050),'동작 참고: CCOHS 작업대 스트레칭 안내 · 순서와 시간은 이 그림용으로 재구성',20,MUTED)
    text(d,(60,2080),'공개 스틱피겨를 참고해 새로 그린 자세 안내도입니다. 치료·재활 처방이 아닙니다.',19,MUTED)
    im.save(ROOT/'desk-stretch-guide.png',optimize=True)
    return im


def frame(step_index, phase):
    # Draw at 1.5x, then downsample for readable Korean and smooth linework.
    im=Image.new('RGB',(1260,1440),BG); d=ImageDraw.Draw(im)
    st=STEPS[step_index]
    text(d,(55,32),'앉아서 스트레칭',48,INK,True)
    d.rounded_rectangle((55,104,1205,171),radius=22,fill='#e1ece6')
    text(d,(630,138),'동작 미리보기 · 실제로는 표시된 시간만큼 천천히',30,TEAL,True,'mm')
    text(d,(60,207),f"{step_index+1:02d}  {st['name']}",45,INK,True)
    text(d,(63,280),st['time'],33,TEAL,True)
    d.rounded_rectangle((55,350,1205,1020),radius=35,fill=PASTELS[step_index])
    if step_index==0: amount=(1-math.cos(phase*4*math.pi))/2
    elif step_index in [1,4]: amount=math.sin(phase*2*math.pi)
    elif step_index in [2,3]: amount=(1-math.cos(phase*2*math.pi))/2
    else: amount=math.sin(phase*2*math.pi)
    figure(d,485,774,1.48,step_index,amount,arrows=True)
    if step_index==5:
        wrist_detail(d,1020,528,1.25,amount)
        text(d,(1020,384),'손목 확대',25,TEAL,True,'mm')
        text(d,(1020,662),'파란 손으로 살짝 잡기',22,TEAL,False,'mm')
    if step_index in [1,4]:
        label='왼쪽 · 가운데 · 오른쪽'
    elif step_index==0: label='올리고 · 편하게 내리고'
    elif step_index==2: label='팔꿈치를 뒤로 · 허리는 그대로'
    elif step_index==3: label='등을 둥글게 · 손은 앞으로'
    else: label='손목만 위아래 · 반대 손으로 가볍게'
    text(d,(630,997),label,30,TEAL,True,'mm')
    text(d,(630,1080),st['lines'][0],34,INK,True,'mm')
    text(d,(630,1142),st['lines'][1],31,MUTED,False,'mm')
    for i in range(6):
        xx=365+i*105
        d.ellipse((xx-29,1232,xx+29,1290),fill=TEAL if i==step_index else '#d8e0da')
        text(d,(xx,1261),str(i+1),28,'white' if i==step_index else MUTED,True,'mm')
    text(d,(630,1353),'양발 바닥에 · 통증·저림·어지럼이 생기면 중단',27,'#873e30',False,'mm')
    return im.resize((840,960),Image.Resampling.LANCZOS)


def animation():
    frames=[]
    per_step=32
    # A 24-second motion preview, not a shortened follow-along timer.
    for n in range(6):
        for k in range(per_step):
            image=frame(n,k/(per_step-1))
            frames.append(image.quantize(colors=128,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE))
        print('rendered step',n+1,flush=True)
    frames[0].save(ROOT/'desk-stretch-motion.gif',save_all=True,append_images=frames[1:],duration=125,loop=0,disposal=2,optimize=False)
    # GIF timing is quantized to 10ms; explicitly inspect actual encoded duration below.
    preview=Image.new('RGB',(840*3,960*2),BG)
    for n in range(6):
        snap=frame(n,.22)
        preview.paste(snap,((n%3)*840,(n//3)*960))
    preview.thumbnail((1260,960),Image.Resampling.LANCZOS)
    preview.save(ROOT/'motion-contact-sheet.jpg',quality=90)


def main():
    poster(); animation()
    with Image.open(ROOT/'desk-stretch-motion.gif') as gif:
        duration=sum(f.info.get('duration',0) for f in ImageSequence.Iterator(gif))
        frames=gif.n_frames
    result={
        'poster':{'file':'desk-stretch-guide.png','size':Image.open(ROOT/'desk-stretch-guide.png').size},
        'gif':{'file':'desk-stretch-motion.gif','size':[840,960],'frames':frames,'duration_seconds':duration/1000},
        'steps':STEPS,
        'sources':[
            {'url':'https://www.ccohs.ca/oshanswers/ergonomics/office/stretching.html','role':'movement and safety reference; sequence and timings adapted'},
            {'url':'https://commons.wikimedia.org/wiki/File:Simple_Stick_Figure.svg','role':'public-domain visual reference; poses newly drawn'},
        ],
    }
    (ROOT/'manifest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result,ensure_ascii=False,indent=2))

if __name__=='__main__':
    main()
