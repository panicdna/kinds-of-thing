"""Generate eight original wellness previews and a nine-theme catalogue.

Only writes make_themes.py's sibling themes/, themes.json, content-notes.txt.
Existing make_art.py and desk-stretch-motion.gif are read-only inputs.
Run with: python make_themes.py
Requires the already-installed Pillow package; no audio, network, or new assets.
"""
from pathlib import Path

import hashlib
import importlib.util
import json
import math
import sys
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'themes'
# Reuse existing typography/rounded-line helpers without creating __pycache__.
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('seated_original_art', ROOT / 'make_art.py')
art = importlib.util.module_from_spec(spec)
spec.loader.exec_module(art)
BG, INK, MUTED, TEAL = art.BG, art.INK, art.MUTED, art.TEAL
W, H, N, MS = 720, 800, 48, 160
COMMON = ('등받이 있는 흔들리지 않는 의자에서 양발을 바닥에 두세요. '
          '통증·저림·어지럼·숨참이 생기면 즉시 중단하고 편히 쉬세요. '
          '반동·과도한 힘·숨 참기는 하지 않습니다. '
          '최근 부상·수술이 있거나 치료 중이면 먼저 의료진에게 확인하세요. '
          '치료·재활 처방이 아닌 짧은 휴식용 동작 미리보기이며 타이머가 아닙니다.')

THEMES = [
    dict(id='stretch', name='앉아서 스트레칭',
         summary='기존에 승인된 어깨·목·등·옆구리·손목 스트레칭 미리보기입니다.',
         instructions=['양발을 바닥에 두고 안정된 의자에 앉아요.',
                       '화면의 여섯 동작을 통증 없는 작은 범위에서 천천히 해요.',
                       '손으로 세게 당기거나 반동을 주지 않고 평소처럼 숨 쉬어요.',
                       'GIF 재생 속도를 따라갈 필요는 없어요.'],
         safety=COMMON, file='desk-stretch-motion.gif'),
    dict(id='meditation', name='명상 · 자연스러운 숨',
         summary='호흡을 바꾸지 않고 지금 느껴지는 숨으로 주의를 돌리는 짧은 휴식입니다.',
         instructions=['등과 어깨를 편히 두고 손을 허벅지 위에 놓아요.',
                       '눈은 편하게 뜨거나 시선을 낮추고 자연스러운 숨을 느껴요.',
                       '생각이 떠오르면 알아차리고 숨의 느낌으로 돌아와요.',
                       '원의 움직임에 호흡을 맞추거나 더 깊게 쉴 필요는 없어요.'],
         safety=COMMON+' 눈 감기가 불편하면 눈을 뜬 채 주변을 보세요.',
         file='themes/meditation.gif'),
    dict(id='pilates', name='필라테스 느낌 · 자세 인식',
         summary='허리를 젖히지 않고 편하게 길어진 자세와 어깨뼈 움직임을 느끼는 초보용 미리보기입니다.',
         instructions=['골반을 의자에 두고 머리 위가 편하게 길어지는 느낌으로 앉아요.',
                       '팔꿈치를 옆구리 가까이에 두고 어깨를 편히 내려요.',
                       '팔꿈치를 아주 조금 뒤로 보냈다가 힘을 풀어요.',
                       '어깨뼈를 세게 조이지 말고 평소처럼 숨 쉬어요.'],
         safety=COMMON+' 정식 필라테스 수업이 아닙니다. 가슴을 과하게 내밀거나 허리를 젖히지 마세요.',
         file='themes/pilates.gif'),
    dict(id='core', name='코어 · 배의 감각 알아차리기',
         summary='편한 자세와 정상 호흡을 유지하며 배 주변에 아주 약한 힘을 주었다 푸는 동작입니다.',
         instructions=['등을 편히 세우고 손을 배 위에 가볍게 올려요.',
                       '평소처럼 숨 쉬면서 배 주변의 움직임을 느껴요.',
                       '자연스럽게 내쉴 때 배에 아주 약한 힘을 주었다 풀어요.',
                       '배를 세게 집어넣거나 몸을 굳히지 않아요.'],
         safety=COMMON+' 숨을 막거나 아래로 힘주지 마세요. 배에 불편함이 있으면 힘주기는 생략하세요.',
         file='themes/core.gif'),
    dict(id='breathing', name='호흡 · 편안하게 내쉬기',
         summary='무리 없이 들이쉬고 가능할 때만 조금 더 여유 있게 내쉬는 호흡 미리보기입니다.',
         instructions=['어깨를 내려놓고 편안하게 앉아요.',
                       '편한 양만 부드럽게 들이쉬어요.',
                       '편하다면 들숨보다 날숨을 조금 더 길고 느긋하게 내보내요.',
                       '숨을 참거나 정해진 초를 채우지 말고 자신의 호흡으로 돌아와요.'],
         safety=COMMON+' 크게 또는 빠르게 반복해서 쉬지 마세요. 숨이 모자라면 즉시 평소 호흡으로 돌아오세요.',
         file='themes/breathing.gif'),
    dict(id='back-seated', name='허리 편한 휴식 · 앉은 자세 바꾸기',
         summary='정답 자세를 강요하지 않고 골반 위치와 어깨를 편하게 바꾸는 앉은 휴식입니다.',
         instructions=['양발을 바닥에 두고 흔들리지 않는 의자에 편히 앉아요.',
                       '엉덩이 위치를 아주 조금 바꿔 자신에게 편한 자리를 찾아요.',
                       '등을 억지로 펴지 말고 어깨의 힘을 부드럽게 풀어요.',
                       '평소처럼 숨 쉬며 불편한 동작은 생략해요.'],
         safety=COMMON+' 허리 통증 치료나 자세 교정이 아닙니다. 통증이 심해지거나 다리 저림·힘 빠짐이 생기면 중단하고 의료진에게 확인하세요. 양쪽 다리 증상·회음부 감각 저하·대소변 변화가 있으면 즉시 응급 진료를 받으세요.',
         file='themes/back-seated.gif'),
    dict(id='back-standing', name='허리 편한 휴식 · 일어나 움직이기',
         summary='스포츠 과학의 동적 스트레칭·가동성 드릴·PNF·플라이오 활성화를 짧게 맛보는 강도 높은 일어서기 휴식입니다.',
         instructions=['주변 2m를 비우고 미끄럽지 않은 바닥에서 신발을 신은 채 가볍게 제자리 걷기로 몸을 데워요.',
                       '동적 스트레칭: 월드 그레이티스트 스트레치(깊은 런지+몸통 회전+팔 뻗기)와 코사크 스쿼트(넓게 벌려 옆으로 깊이 앉기)를 좌우 번갈아 해요.',
                       '가동성·PNF: 90/90 고관절 전환과 햄스트링 수축-이완(5초 힘줬다 풀고 더 뻗기)을 해요.',
                       '플라이오 활성화: 하이니, 스케이터 점프, 점핑잭으로 빠르게 끌어올린 뒤 호흡을 이어가며 천천히 마무리해요.'],
         safety='스포츠 과학식 스트레칭 맛보기로 일반 휴식 동작보다 강도가 높습니다. 주변 바닥을 비우고, 가볍게 몸을 데운 뒤 범위와 속도를 단계적으로 올리세요. 숨은 참지 말고 이어가세요. 통증·저림·어지럼·숨참이 생기면 즉시 중단하고 쉬세요. 점프·깊은 런지가 부담되면 제자리 걷기와 작은 범위로 낮추세요. 최근 부상·수술·심혈관 질환·임신이 있거나 치료 중이면 먼저 의료진에게 확인하세요. 치료·재활·운동 처방이 아닌 동작 미리보기이며 타이머가 아닙니다. 다리 저림·힘 빠짐이 생기면 즉시 중단하고 의료진에게 확인하세요. 양쪽 다리 증상·회음부 감각 저하·대소변 변화가 있으면 즉시 응급 진료를 받으세요.',
         file='themes/back-standing.gif'),
    dict(id='voice', name='발성 · 아주 가벼운 허밍',
         summary='편한 음높이와 작은 일상 목소리 크기로 짧게 허밍하고 쉬는 미리보기입니다. 음성 안내는 선택으로 켤 수 있어요.',
         instructions=['턱과 목을 편히 두고 평소처럼 숨을 들이쉬어요.',
                       '입술을 가볍게 모으고 편한 음높이에서 짧게 음~ 해요.',
                       '목을 누르거나 소리를 밀어내지 말고 바로 쉬어요.',
                       '쉰 목소리이거나 목이 피곤하면 허밍 없이 쉬어요.'],
         safety=COMMON+' 속삭임·고음·큰 소리를 피하세요. 목이 아프거나 목소리가 쉬면 중단하세요. 발성 치료가 아닙니다.',
         file='themes/voice.gif'),
]

ORDER = ['stretch','meditation','pilates','core','breathing','voice','back-seated','back-standing']
THEMES.sort(key=lambda item: ORDER.index(item['id']))
LOOK = {
    'meditation': ('#e4f1eb', '자연스러운 숨에 주의를 두어요', '손은 허벅지에 · 호흡은 그대로', '관찰', '돌아오기'),
    'pilates': ('#f9ebdf', '팔꿈치를 조금 뒤로 · 다시 편하게', '허리를 젖히지 않고 평소처럼 호흡', '편히 길게', '가볍게 풀기'),
    'core': ('#e4f1eb', '자연스러운 날숨에 아주 약한 힘', '숨을 멈추지 말고 · 힘은 바로 풀어요', '감각 느끼기', '아주 약하게'),
    'breathing': ('#e8eef8', '편한 만큼 들이쉬고 · 여유 있게 내쉬기', '숨 참기 없이 · 자신의 속도로', '부드러운 들숨', '느긋한 날숨'),
    'back-seated': ('#e4f1eb', '엉덩이 위치를 편하게 · 어깨 힘 빼기', '정답 자세는 없어요 · 억지로 펴지 않기', '편한 자리', '조금 바꾸기'),
    'back-standing': ('#f9ebdf', '동적 스트레칭 · 가동성 · PNF · 플라이오', '몸을 데운 뒤 · 범위와 속도는 단계적으로', '', ''),
    'voice': ('#f9ebdf', '편한 음높이로 짧게 음~ · 쉬기', '목에 힘주지 않기 · 이 GIF는 무음', '편히 숨쉬기', '가볍게 음~'),
}


def smooth(x):
    return (1 - math.cos(math.pi * x)) / 2


def fit_text(d, xy, value, size=24, color=INK, bold=False, anchor=None, max_width=640):
    while d.textbbox((0, 0), value, font=art.font(size, bold))[2] > max_width:
        size -= 1
    art.text(d, xy, value, size, color, bold, anchor)


def draw_figure(d, theme, t):
    """All pelvis/leg/foot coordinates stay constant in every generated frame."""
    x, y, s = 286, 451, 1.03
    wave = (1 - math.cos(2 * math.pi * t)) / 2
    breath = wave
    if theme == 'breathing':
        breath = smooth(t / .4) if t < .4 else 1 - smooth((t - .4) / .6)
    theta = .095 * math.sin(2 * math.pi * t) if theme == 'yoga' else 0
    rise = 2.4 * breath if theme in ('meditation', 'core', 'breathing', 'voice') else 0
    def pt(p):
        return (x + s * p[0], y + s * p[1])
    def poly(points, color=INK, width=5):
        art.line(d, [pt(p) for p in points], color, max(1, round(width*s)))
    def circ(p, r, fill=BG, outline=INK, width=3):
        px, py = pt(p)
        d.ellipse((px-r*s, py-r*s, px+r*s, py+r*s), fill=fill, outline=outline, width=width)
    def box(coords, fill, outline=None):
        a,b,c,e = coords
        d.rounded_rectangle((x+a*s,y+b*s,x+c*s,y+e*s),radius=8,fill=fill,outline=outline,width=2)
    def upper(p):
        a,b = p
        return (a*math.cos(theta)-b*math.sin(theta), a*math.sin(theta)+b*math.cos(theta)-rise)
    # Stable, non-wheeled chair and fixed floor / feet.
    poly([(-151,130),(169,130)], '#bbcfc8', 2)
    box((-84,-91,84,9), '#dde8e1', '#aec4b9')
    box((-88,4,88,17), '#b9cfc8')
    for sign in (-1,1):
        poly([(sign*77,17),(sign*77,129)], '#8ea9a2', 4)
        poly([(0,0),(sign*47,29),(sign*47,127),(sign*70,127)], INK, 6)
    if theme == 'meditation':
        # Breath awareness halo is an illustration, not a prescribed pace.
        r = 72+8*breath
        px,py = pt((0,-103))
        d.ellipse((px-r,py-r,px+r,py+r),outline='#b6d9cd',width=3)
    if theme == 'core':
        radius = 29-4*wave
        circ((0,-43),radius, '#d1e7d9', '#78ad93',2)
    # A neutral spine: no sit-ups, back extension, or neck flexion.
    poly([(0,0),upper((0,-112)),upper((0,-137))],INK,6)
    poly([upper((-28,-110)),upper((28,-110))],INK,5)
    if theme == 'yoga':
        # Arms travel in a comfortable arc rather than abruptly jumping poses.
        lift = .65 + .35*math.sin(math.pi*t)**2
        for sign in (-1,1):
            elbow=(sign*(47+14*(1-lift)),-67-89*lift)
            wrist=(sign*(38-21*lift),-15-194*lift)
            poly([upper((sign*28,-110)),upper(elbow),upper(wrist)],INK,5)
        # Quiet path cue over the figure, never indicating a forced range.
        cx,cy=pt((0,-196))
        d.arc((cx-95,cy-42,cx+95,cy+46),200,340,fill='#a4b9d5',width=2)
    elif theme == 'pilates':
        for sign in (-1,1):
            sh=(sign*28,-110)
            elbow=(sign*(45+12*wave),-60-5*wave)
            wrist=(sign*(18+6*wave),-61-5*wave)
            poly([sh,elbow,wrist],INK,5)
            # Small inward-facing shoulder-blade cues, not maximal squeezing.
            cx,cy=pt((sign*(19-4*wave),-94))
            d.arc((cx-7,cy-14,cx+7,cy+14),40 if sign<0 else 220,140 if sign<0 else 320,fill=TEAL,width=3)
    elif theme == 'core':
        for sign in (-1,1):
            poly([upper((sign*28,-110)),(sign*52,-66),(sign*(16-3*wave),-44)],INK,5)
            circ((sign*(16-3*wave),-44),3,TEAL,TEAL,1)
    elif theme == 'breathing':
        for sign in (-1,1):
            poly([upper((sign*28,-110)),upper((sign*48,-65)),upper((sign*(18+3*breath),-75))],INK,5)
        # Small rib outlines emphasize that the posture stays easy.
        for sign in (-1,1):
            cx,cy=pt((sign*15,-84-rise))
            d.arc((cx-12-2*breath,cy-21,cx+12+2*breath,cy+21),70 if sign<0 else 250,290 if sign<0 else 110,fill='#7aa7b7',width=2)
    else:
        for sign in (-1,1):
            poly([upper((sign*28,-110)),upper((sign*44,-55)),(sign*39,-4)],INK,5)
    head=upper((0,-167))
    circ(head,29,'#fffdf7',INK,4)
    # Relaxed open/downward eyes; no compulsory eye closure.
    for sign in (-1,1):
        if theme == 'meditation':
            poly([(head[0]+sign*8-3,head[1]-2),(head[0]+sign*8+3,head[1]-1)],INK,1.6)
        else:
            circ((head[0]+sign*8,head[1]-3),1.5,INK,INK,1)
    poly([(head[0]-6,head[1]+10),(head[0]+6,head[1]+10)],INK,2)
    circ((0,0),4,TEAL,TEAL,1)
    # A distinct right-hand illustration for each theme.
    cx,cy=550,370
    d.ellipse((cx-79,cy-79,cx+79,cy+79),fill='#fffdf7',outline='#c2d7cf',width=2)
    if theme == 'meditation':
        for k in range(3):
            radius=22+k*15+3*breath
            d.ellipse((cx-radius,cy-radius,cx+radius,cy+radius),outline=['#528e7b','#8ab8a3','#c2d7c6'][k],width=3 if k==0 else 2)
        d.ellipse((cx-5,cy-5,cx+5,cy+5),fill=TEAL)
    elif theme == 'yoga':
        art.line(d,[(cx,cy+41),(cx,cy-21)],TEAL,5)
        spread=29+6*math.sin(math.pi*t)
        art.line(d,[(cx-spread,cy-45),(cx,cy-21),(cx+spread,cy-45)],TEAL,5)
        d.arc((cx-52,cy-69,cx+52,cy-4),188,352,fill='#879fc3',width=3)
    elif theme == 'pilates':
        for sy in range(cy-47,cy+48,15):
            d.line((cx,sy,cx,sy+7),fill='#91a39d',width=2)
        art.line(d,[(cx-39,cy-20),(cx,cy-27),(cx+39,cy-20)],TEAL,4)
        art.line(d,[(cx-27,cy+23),(cx,cy+33),(cx+27,cy+23)],TEAL,4)
        for sign in (-1,1):
            bx=cx+sign*(20-5*wave)
            d.arc((bx-9,cy-10,bx+9,cy+20),50 if sign<0 else 230,130 if sign<0 else 310,fill='#bb8665',width=3)
    elif theme == 'core':
        rr=40-7*wave
        d.ellipse((cx-rr,cy-rr,cx+rr,cy+rr),fill='#e1efe5',outline='#78ad93',width=3)
        art.line(d,[(cx,cy-24),(cx,cy+24)],TEAL,3)
        for sign in (-1,1):
            art.line(d,[(cx+sign*22,cy-16),(cx+sign*17,cy),(cx+sign*22,cy+16)],TEAL,3)
    elif theme == 'breathing':
        rr=24+29*breath
        d.ellipse((cx-rr,cy-rr,cx+rr,cy+rr),fill='#d7e7ef',outline='#578a9f',width=3)
        fit_text(d,(cx,cy),'숨',26,TEAL,True,'mm',100)
    else:
        humming=.24<t<.64
        fit_text(d,(cx-11,cy),'음~' if humming else '쉼',25,TEAL,True,'mm',100)
        if humming:
            for k in range(3):
                rr=13+k*13+3*math.sin(8*math.pi*t)
                d.arc((cx+12-rr,cy-rr,cx+12+rr,cy+rr),305,55,fill='#b48666',width=2)
            hx,hy=pt(head)
            for k in range(2):
                rr=10+k*10
                d.arc((hx+37-rr,hy-rr,hx+37+rr,hy+rr),305,55,fill=TEAL,width=2)
    if theme == 'breathing': label=LOOK[theme][3 if t<.4 else 4]
    elif theme == 'voice': label=LOOK[theme][4 if .24<t<.64 else 3]
    elif theme == 'yoga': label='왼쪽 · 가운데 · 오른쪽'
    elif theme == 'core': label=LOOK[theme][4 if .28<t<.7 else 3]
    else: label=LOOK[theme][3 if t<.5 else 4]
    fit_text(d,(550,472),label,22,TEAL,True,'mm',205)
    fit_text(d,(550,510),'양발은 바닥에',18,MUTED,False,'mm',205)


# Sports-science stretching previews: (label, pose, category). Joint coordinates
# on the 720x800 canvas; floor is y=588. Poses are interpolated between keys.
def _pose(hip, sh, head, kl, fl, kr, fr, hl, hr):
    return dict(hip=hip, sh=sh, head=head, kl=kl, fl=fl, kr=kr, fr=fr, hl=hl, hr=hr)

_STAND = _pose((286,430),(286,320),(286,265),(272,510),(266,588),(302,510),(306,588),(252,400),(320,400))
SPORT_KEYS = [
    ('몸 데우기 · 제자리 걷기', _STAND, '워밍업'),
    ('월드 그레이티스트 스트레치', _pose((262,492),(318,402),(342,350),(352,505),(354,588),(205,548),(148,586),(345,540),(350,262)), '동적 스트레칭'),
    ('코사크 스쿼트 · 옆으로 깊이', _pose((230,500),(232,392),(232,336),(204,532),(188,588),(300,544),(372,588),(256,430),(262,430)), '가동성 드릴'),
    ('햄스트링 PNF · 힘줬다 풀기', _pose((236,440),(204,345),(188,290),(330,510),(392,588),(215,520),(180,588),(296,470),(300,500)), 'PNF'),
    ('하이니 · 빠른 무릎 올리기', _pose((286,430),(286,322),(286,267),(344,408),(330,470),(300,510),(306,588),(320,360),(250,390)), '플라이오 활성화'),
    ('스케이터 점프 · 옆으로 뛰기', _pose((250,455),(290,352),(310,300),(280,520),(300,588),(215,525),(160,560),(205,410),(350,430)), '플라이오 활성화'),
    ('점핑잭 · 팔 벌려 올리기', _pose((286,415),(286,306),(286,251),(240,500),(205,570),(332,500),(367,570),(210,190),(362,190)), '심박 올리기'),
    ('천천히 숨 고르며 마무리', _STAND, '쿨다운'),
]


def draw_sport(d, t):
    n = len(SPORT_KEYS)
    idx = min(int(t * n), n - 1)
    frac = t * n - idx
    prev, cur = SPORT_KEYS[idx - 1][1], SPORT_KEYS[idx][1]
    e = smooth(min(1, frac / 0.55))
    p = {k: (prev[k][0] + (cur[k][0] - prev[k][0]) * e, prev[k][1] + (cur[k][1] - prev[k][1]) * e) for k in cur}
    name = SPORT_KEYS[idx][0]
    if name.startswith(('하이니', '점핑잭', '스케이터')) and frac > .55:
        hop = abs(math.sin(frac * 4 * math.pi)) * 14
        p = {k: (v[0], v[1] - hop) for k, v in p.items()}
    line = lambda pts, c=INK, w=6: art.line(d, pts, c, w)
    line([(126, 591), (454, 591)], '#bbcfc8', 2)
    line([p['hip'], p['kl'], p['fl'], (p['fl'][0] + 20, p['fl'][1])], '#71858a', 5)
    line([p['hip'], p['kr'], p['fr'], (p['fr'][0] + 20, p['fr'][1])], INK, 6)
    line([p['hip'], p['sh'], (p['head'][0], p['head'][1] + 29)], INK, 6)
    line([p['sh'], p['hl']], '#71858a', 5)
    line([p['sh'], p['hr']], INK, 5)
    hx, hy = p['head']
    d.ellipse((hx - 29, hy - 29, hx + 29, hy + 29), fill='#fffdf7', outline=INK, width=4)
    d.ellipse((hx + 8, hy - 5, hx + 11, hy - 2), fill=INK)
    d.ellipse((p['hip'][0] - 4, p['hip'][1] - 4, p['hip'][0] + 4, p['hip'][1] + 4), fill=TEAL)
    return name


def draw_back(d, theme, t):
    """Comfort breaks, not alignment correction or a back-pain protocol."""
    wave = (1-math.cos(2*math.pi*t))/2
    def line(points, color=INK, width=5):
        art.line(d,points,color,width)
    def dot(p,r=4,color=TEAL):
        x,y=p
        d.ellipse((x-r,y-r,x+r,y+r),fill=color)
    if theme == 'back-seated':
        # Fixed feet, tiny pelvic reposition and relaxed (not rigid) shoulders.
        hip=(286+5*math.sin(2*math.pi*t),459)
        shoulders=(hip[0]+3*wave,347+4*wave)
        head=(shoulders[0]+2*wave,292+4*wave)
        line([(129,588),(450,588)],'#bbcfc8',2)
        d.rounded_rectangle((202,365,370,468),radius=9,fill='#dde8e1',outline='#aec4b9',width=2)
        d.rounded_rectangle((198,463,374,477),radius=6,fill='#b9cfc8')
        for sign in (-1,1):
            line([(286+sign*78,478),(286+sign*78,589)],'#8ea9a2',4)
            line([hip,(286+sign*48,490),(286+sign*48,585),(286+sign*72,585)],INK,6)
        # Soft, slightly curved torso, without an ideal-spine before/after story.
        line([hip,(hip[0]+5,407),shoulders,(head[0],head[1]+29)],INK,6)
        for sign in (-1,1):
            shoulder=(shoulders[0]+sign*28,shoulders[1])
            line([shoulders,shoulder,(shoulders[0]+sign*45,406+2*wave),(286+sign*40,451)],INK,5)
        d.ellipse((head[0]-29,head[1]-29,head[0]+29,head[1]+29),fill='#fffdf7',outline=INK,width=4)
        for sign in (-1,1): dot((head[0]+sign*8,head[1]-3),1.5,INK)
        line([(head[0]-5,head[1]+10),(head[0]+5,head[1]+10)],INK,2)
        dot(hip)
        # Small lateral comfort cue, not a target or corrective ruler.
        line([(hip[0]-18,448),(hip[0]+18,448)],'#78ad93',2)
        label='편한 자리로 조금'
    else:
        label=draw_sport(d,t)
    cx,cy=550,370
    d.ellipse((cx-79,cy-79,cx+79,cy+79),fill='#fffdf7',outline='#c2d7cf',width=2)
    if theme == 'back-seated':
        for sign in (-1,1):
            bx=cx+sign*22+4*math.sin(2*math.pi*t)
            d.arc((bx-12,cy-30,bx+12,cy+30),0,330,fill='#78ad93',width=3)
        dot((cx,cy),5)
        side_note='발은 바닥에 그대로'
    else:
        side_note=SPORT_KEYS[min(int(t*len(SPORT_KEYS)),len(SPORT_KEYS)-1)][2]
    fit_text(d,(550,472),label,19 if theme=='back-standing' else 21,TEAL,True,'mm',212)
    fit_text(d,(550,510),side_note,18,MUTED,False,'mm',212)


def frame(meta, t):
    ident=meta['id']
    im=Image.new('RGB',(W,H),BG)
    d=ImageDraw.Draw(im)
    heading='DESK BREAK  /  COMFORT & MOVEMENT' if ident=='back-standing' else 'DESK BREAK  /  SEATED WELLNESS'
    fit_text(d,(38,24),heading,15,TEAL,True)
    fit_text(d,(36,58),meta['name'],34,INK,True,max_width=650)
    d.rounded_rectangle((38,119,682,157),radius=13,fill='#e0eae2')
    fit_text(d,(360,138),'동작 미리보기 · 타이머가 아닙니다',21,TEAL,True,'mm')
    d.rounded_rectangle((38,178,682,616),radius=24,fill=LOOK[ident][0])
    fit_text(d,(64,195),'작은 움직임 · 편안한 호흡',18,MUTED)
    if ident.startswith('back-'):
        draw_back(d,ident,t)
    else:
        draw_figure(d,ident,t)
    fit_text(d,(360,652),LOOK[ident][1],25,INK,True,'mm')
    fit_text(d,(360,692),LOOK[ident][2],23,MUTED,False,'mm')
    d.rounded_rectangle((38,734,682,779),radius=12,fill='#f3e0d8')
    warning='고강도 · 몸을 데운 뒤 · 통증·저림·어지럼이면 중단' if ident=='back-standing' else '통증 증가·다리 저림·힘 빠짐이 생기면 중단' if ident.startswith('back-') else '통증·저림·어지럼·숨참이 생기면 중단'
    fit_text(d,(360,756),warning,20,'#873e30',False,'mm')
    return im


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_gif(path):
    with Image.open(path) as im:
        info=dict(file=str(path.relative_to(ROOT)),size=list(im.size),frames=im.n_frames,
                  duration_ms=0,loop=im.info.get('loop'),bytes=path.stat().st_size,sha256=sha(path))
        uniques=set()
        for i in range(im.n_frames):
            im.seek(i)
            info['duration_ms']+=im.info.get('duration',0)
            uniques.add(hashlib.sha256(im.convert('RGB').tobytes()).digest())
        info['unique_frames']=len(uniques)
    assert info['frames']>1 and info['unique_frames']>1 and info['loop']==0
    return info


def main():
    original={name:sha(ROOT/name) for name in ('make_art.py','desk-stretch-motion.gif')}
    assert (ROOT/'desk-stretch-motion.gif').is_file()
    OUT.mkdir(exist_ok=True)

    reports=[]
    for meta in THEMES[1:]:
        frames=[frame(meta,k/N) for k in range(N)]

        palette_frames=[im.quantize(colors=96,method=Image.Quantize.MEDIANCUT,dither=Image.Dither.NONE) for im in frames]
        target=ROOT/meta['file']
        palette_frames[0].save(target,save_all=True,append_images=palette_frames[1:],
                               duration=MS,loop=0,disposal=2,optimize=False)
        report=verify_gif(target)
        assert report['size']==[W,H] and report['duration_ms']==N*MS
        reports.append(report)
        print(json.dumps(report,ensure_ascii=False),flush=True)
    # Eight rows, three phases: inspect actual encoded GIFs, not renders.
    sheet=Image.new('RGB',(1080,400*(len(THEMES)-1)),BG)
    for row,meta in enumerate(THEMES[1:]):
        with Image.open(ROOT/meta['file']) as gif:
            fractions=(.18,.30,.55) if meta['id']=='back-standing' else (.08,.38,.72)
            for col,fraction in enumerate(fractions):
                elapsed=0
                for idx in range(gif.n_frames):
                    gif.seek(idx)
                    elapsed+=gif.info.get('duration',0)
                    if elapsed>fraction*N*MS:
                        break
                thumb=gif.convert('RGB').resize((360,400),Image.Resampling.LANCZOS)
                sheet.paste(thumb,(col*360,row*400))
    sheet.save(OUT/'contact-sheet.png',optimize=True)
    ids=[x['id'] for x in THEMES]
    assert ids==ORDER
    assert len(set(ids))==9
    for meta in THEMES:
        assert set(meta)=={'id','name','summary','instructions','safety','file'}
        assert isinstance(meta['instructions'],list) and meta['instructions']
        assert not Path(meta['file']).is_absolute()
        assert (ROOT/meta['file']).is_file()
    assert all(sha(ROOT/name)==digest for name,digest in original.items())
    # Catalogue is published only after every referenced file exists and decodes.
    (ROOT/'themes.json').write_text(json.dumps(THEMES,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    decoded=json.loads((ROOT/'themes.json').read_text(encoding='utf-8'))
    assert len(decoded)==9 and all((ROOT/m['file']).is_file() for m in decoded)
    result=dict(catalogue_entries=len(decoded),new_gifs=len(reports),referenced_gifs=9,
                new_gif_resolution=[W,H],files=reports,original_inputs_unchanged=True,
                original_input_sha256=original,contact_sheet='themes/contact-sheet.png',
                preview_only=True,audio=False,mandatory_breath_holds=False)
    (OUT/'verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    notes='''SEATED WELLNESS CONTENT NOTES / 앉아서 쉬는 테마 안내

Catalogue: themes.json is a JSON ARRAY with exactly nine entries.
Fields: id, name, summary, instructions (array), safety (string), file.
Paths are relative to this directory. stretch retains the original approved GIF.
The other eight files are original drawings generated by make_themes.py, reusing
only original make_art.py typography and rounded-line helpers. No stock images,
third-party characters, audio, music, synthetic speech, or network resources.
Pillow and existing Windows Malgun fonts are the only rendering requirements.

Visual concept: cream poster, charcoal seated stick figure, teal guidance,
pastel cards and a stable non-wheeled chair. Feet stay planted in all seated
themes; the standing theme adds supported, low alternating foot lifts. Meditation
shows unforced breath awareness, yoga gentle arm elevation and side inclinations,
Pilates-inspired preview small elbow/scapular control, core gentle abdominal
awareness, breathing a soft expanding/contracting circle with a longer outward
phase, voice a brief closed-lip hum followed by ordinary rest. The original
stretch preview is not modified. No full yoga/Pilates course is represented.
back-seated is a small, comfortable pelvic reposition and shoulder relaxation,
not an ideal-spine correction. back-standing shows a higher-intensity sports-science
sequence: warm-up, world's-greatest stretch, Cossack squat, PNF hamstring,
high knees, skater hops, jumping jacks and cool-down. Neither
is a back-pain treatment. Stop for increased pain or leg numbness/weakness;
recent injury/surgery requires clinician advice. Bilateral leg symptoms, loss
of feeling around the genitals/anus, or bladder/bowel changes need urgent care.

All GIFs are movement previews, NOT exercise timers or prescribed breathing
rates. Frame-loop duration is a technical property, not a target to follow.
The meditation illustration does not ask users to pace or deepen the breath.
Breathing preview's slightly longer exhalation is an optional creative cue,
not an NHS-prescribed ratio or mandatory count. Users may return to normal
breathing at any point. Voice preview is silent, at a comfortable imagined
pitch; it does not prescribe a note, whisper, loud sound, or maximal breath.
No exercise efficacy or treatment benefit is claimed. All movements may be
skipped. Stop for pain, tingling, dizziness, breathlessness or vocal discomfort.
Recent injury, surgery, or ongoing treatment calls for individual clinical advice.

No default asset or instruction includes forced inhalation, hyperventilation,
Valsalva/straining, or breath holding. A separately implemented optional
30-second breath-hold timer is outside this asset set and is not required by
any theme. These assets do not recommend that timer or assert it is safe.

Primary references (guidance paraphrased, no copied illustrations):
1. CCOHS, Office Ergonomics - Stretching - At the Workstation
   https://www.ccohs.ca/oshanswers/ergonomics/office/stretching.html
   Supports normal breathing, avoiding bouncing/excessive pulling or pushing,
   stopping for pain/discomfort, and gentle upper-body stretch concepts.
   Seated adaptations, exact poses, labels and loop timing are original.
2. NHS, Breathing exercises for stress
   https://www.nhs.uk/mental-health/self-help/guides-tools-and-activities/breathing-exercises-for-stress/
   Supports comfortable supported sitting, feet flat, gentle regular breathing
   without forcing. It does not specifically endorse this artwork or a longer
   exhale ratio, meditation sequence, core action or Pilates-labelled routine.
3. NIH/NIDCD, Taking Care of Your Voice
   https://www.nidcd.nih.gov/health/taking-care-your-voice
   Supports avoiding extremes of pitch/loudness and resting a hoarse or tired
   voice. The short comfortable-pitch humming preview is an original cautious
   activity, not a clinically validated vocal-therapy protocol.
4. NHS, Back pain
   https://www.nhs.uk/conditions/back-pain/
   Supports staying active as comfortable and stopping exercises if pain worsens.
   Its urgent red flags inform the back-theme safety metadata. The exact chair
   animations are original comfort breaks, not NHS-prescribed exercises or
   posture correction advice.

Verification: themes/verification.json records actual decoded GIF frame counts,
unique frames, sizes, byte counts, durations, infinite looping and SHA-256 hashes.
themes/contact-sheet.png contains actual decoded GIF frames near 8%, 38%, 72%
of each loop (8%, 38%, 50% for standing to show seated/left/right step states).
Inspect this sheet for text legibility and thematic differences.
Existing make_art.py and desk-stretch-motion.gif SHA-256 values are checked
before and after generation; neither is rewritten.
'''
    (ROOT/'content-notes.txt').write_text(notes,encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='files'},ensure_ascii=False,indent=2))


if __name__=='__main__':
    main()
