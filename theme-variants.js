/* Sub-items (per theme id) shown inside a theme's popup. Data only: no network requests.
 * type "gif"   : the original approved animation of the theme (themes.json "file").
 * type "video" : a public YouTube video embedded via youtube-nocookie; youtubeId must be 11 chars [A-Za-z0-9_-].
 * wide: true marks a landscape (16:9) video; the default is a vertical Shorts player.
 * To add one, append an object here. Videos are only embedded or linked, never downloaded or re-edited.
 */
window.THEME_VARIANTS = {
  stretch: [
  { id: 'basic', type: 'gif', title: '기본 · 어깨·목·등·옆구리·손목 6동작', author: '' },
  { id: 'yt-Md24PbVP72U', type: 'video', title: '의자에서 하는 허리디스크 재활운동', author: '운동정선생', youtubeId: 'Md24PbVP72U', url: 'https://www.youtube.com/shorts/Md24PbVP72U' },
  { id: 'yt--yOiTWEgU_Y', type: 'video', title: '의자에 앉아서 하는 운동 (Chairs Abs Workout)', author: '임피티 IMPT DIET', youtubeId: '-yOiTWEgU_Y', url: 'https://www.youtube.com/shorts/-yOiTWEgU_Y' },
  { id: 'yt--_Oqek7ARlk', type: 'video', title: '앉아있을 때 허리가 아프다면? 필수 스트레칭', author: '관절사용설명서', youtubeId: '-_Oqek7ARlk', url: 'https://www.youtube.com/shorts/-_Oqek7ARlk' },
  { id: 'yt-2DRvc74GgYM', type: 'video', wide: true, title: '앉아서 하는 스트레칭 루틴! 의자 스트레칭', author: '관절사용설명서', youtubeId: '2DRvc74GgYM', url: 'https://www.youtube.com/watch?v=2DRvc74GgYM' },
  { id: 'yt-5fnEEzi_ev0', type: 'video', wide: true, title: '앉아서 하는 요가 스트레칭 · 수험생, 직장인을 위한 의자 스트레칭 따라하기', author: '에일린 mind yoga', youtubeId: '5fnEEzi_ev0', url: 'https://www.youtube.com/watch?v=5fnEEzi_ev0' },
  { id: 'yt-QteVMsNU3dk', type: 'video', title: '의자 하나로 전신 운동 하는 방법', author: '홈트하루', youtubeId: 'QteVMsNU3dk', url: 'https://www.youtube.com/shorts/QteVMsNU3dk' },
  { id: 'yt-ngbWg9WUMoE', type: 'video', title: '의자에서 할 수 있는 초간단 스트레칭', author: '예찌쌤', youtubeId: 'ngbWg9WUMoE', url: 'https://www.youtube.com/shorts/ngbWg9WUMoE' },
  { id: 'yt-VF20iXwq29U', type: 'video', wide: true, title: '앉아서 하는 어르신 5분 상체스트레칭', author: '가평군체육회 tv', youtubeId: 'VF20iXwq29U', url: 'https://www.youtube.com/watch?v=VF20iXwq29U' },
  { id: 'yt-dNZ2aJp2rwI', type: 'video', title: '바로 효과 보는 초간단 목어깨 스트레칭', author: '문쌤재활', youtubeId: 'dNZ2aJp2rwI', url: 'https://www.youtube.com/shorts/dNZ2aJp2rwI' },
  { id: 'yt-02pSlIL7lig', type: 'video', title: '의자에 앉아서 하는 구부정한 자세 펴기 · 허리 운동 (힙힌지)', author: '함께해요 운동채널', youtubeId: '02pSlIL7lig', url: 'https://www.youtube.com/shorts/02pSlIL7lig' },
  { id: 'yt-A50ClACVyFE', type: 'video', title: '앉아서 하는 초간단 햄스트링 스트레칭 방법', author: '건강의학전문채널 하이닥', youtubeId: 'A50ClACVyFE', url: 'https://www.youtube.com/shorts/A50ClACVyFE' },
  { id: 'yt-Q7ogtZAplLM', type: 'video', wide: true, title: '스트레칭 · 의자에 앉아서 하는 12분 요가', author: '서리요가seoriyoga', youtubeId: 'Q7ogtZAplLM', url: 'https://www.youtube.com/watch?v=Q7ogtZAplLM' },
  { id: 'yt-u5GPq0CC7U8', type: 'video', wide: true, title: '무릎 통증 없이 앉아서 하는 초보자 하체운동 루틴', author: '관절사용설명서', youtubeId: 'u5GPq0CC7U8', url: 'https://www.youtube.com/watch?v=u5GPq0CC7U8' },
  { id: 'yt-HCjvbjbCSsk', type: 'video', title: '의자에서 끝내는 좌골·골반·허리 루틴 3가지 (3 Chair Stretches)', author: '바디땅콩Tv', youtubeId: 'HCjvbjbCSsk', url: 'https://www.youtube.com/shorts/HCjvbjbCSsk' }
],
  // Community card "눈 쉬기 20-20-20": its own text steps are replaced by this video (the card itself stays).
  'c-u-eye-20-20-20': [
    { id: 'yt-356wdTdVaTs', type: 'video', title: '모니터 오래 본 날, 눈 쉬기 1분', author: '하루힐링명상', youtubeId: '356wdTdVaTs', url: 'https://www.youtube.com/shorts/356wdTdVaTs' },
    { id: 'yt-2oojWWfappA', type: 'video', title: '눈이 피곤하고 무거울 때 따라 하기', author: '오상신경외과의원', youtubeId: '2oojWWfappA', url: 'https://www.youtube.com/shorts/2oojWWfappA' }
  ],
  // 릴렉스 음향: sound-only relaxation, shown as a video.
  relax: [
    { id: 'yt-5yApImDt_Fk', type: 'video', title: '릴렉스 음향 · 양측 음향 릴렉스 뮤직', author: 'octowave: Sleep, Focus & Bible', youtubeId: '5yApImDt_Fk', url: 'https://www.youtube.com/shorts/5yApImDt_Fk' },
    { id: 'yt-srz5h0UQ6tc', type: 'video', title: '10분 명상 음악', author: '힐링위드사운드', youtubeId: 'srz5h0UQ6tc', url: 'https://www.youtube.com/shorts/srz5h0UQ6tc' },
    { id: 'yt-Yma-nUogjao', type: 'video', title: '4-2-6 호흡법', author: '가치요가 Gachi Yoga', youtubeId: 'Yma-nUogjao', url: 'https://www.youtube.com/shorts/Yma-nUogjao' }
  ],
  // 요리 레시피: a short cooking video as a break.
  recipe: [
    { id: 'yt-s10KwYn064E', type: 'video', title: '양배추 오코노미야끼 (노밀가루)', author: '혹시PICK', youtubeId: 's10KwYn064E', url: 'https://www.youtube.com/shorts/s10KwYn064E' }
  ],
  // Voice: the original picture/timer is replaced by this video.
  voice: [
    { id: 'yt-88TBCVp-Tlc', type: 'video', title: '숨쉴 때 편안한 호흡 발성 연습', author: '윤하의 Joyful Voice', youtubeId: '88TBCVp-Tlc', url: 'https://www.youtube.com/shorts/88TBCVp-Tlc' },
    { id: 'yt-x6y6SPzCQXQ', type: 'video', wide: true, title: '호흡 연습법 STEP 1', author: 'VOCAL LOGIC', youtubeId: 'x6y6SPzCQXQ', url: 'https://www.youtube.com/watch?v=x6y6SPzCQXQ' },
    { id: 'yt-YdBUnWJBYAo', type: 'video', wide: true, title: '호흡 연습법 STEP 2', author: 'VOCAL LOGIC', youtubeId: 'YdBUnWJBYAo', url: 'https://www.youtube.com/watch?v=YdBUnWJBYAo' },
    { id: 'yt-YLfFw6IuVqc', type: 'video', wide: true, title: '흉복식호흡 연습법', author: 'VOCAL LOGIC', youtubeId: 'YLfFw6IuVqc', url: 'https://www.youtube.com/watch?v=YLfFw6IuVqc' },
    { id: 'yt-43MKF1bL_mM', type: 'video', wide: true, title: '복식호흡 연습 · 3회 내쉬고 들이쉬기', author: 'VOCAL LOGIC', youtubeId: '43MKF1bL_mM', url: 'https://www.youtube.com/watch?v=43MKF1bL_mM' }
  ],
  meditation: [
    { id: 'yt-6Qu5E3MrVH8', type: 'video', wide: true, title: '5분 명상 · 호흡명상 가이드', author: '에일린 mind yoga', youtubeId: '6Qu5E3MrVH8', url: 'https://www.youtube.com/watch?v=6Qu5E3MrVH8' },
    { id: 'yt-LaM3-_ntnjU', type: 'video', wide: true, title: '복잡한 생각, 5분 만에 정리하는 빼기명상 가이드', author: '마음수련 명상', youtubeId: 'LaM3-_ntnjU', url: 'https://www.youtube.com/watch?v=LaM3-_ntnjU' },
    { id: 'yt-Og_Qli3M_nE', type: 'video', wide: true, title: '명상 음악 · Reiki Healing Music', author: 'Reiki Flow', youtubeId: 'Og_Qli3M_nE', url: 'https://www.youtube.com/watch?v=Og_Qli3M_nE' }
  ],
  'back-standing': [
    { id: 'yt-N-V53dmXJ9M', type: 'video', title: '허리통증 잡는 초간단 장요근 스트레칭 (의자 활용)', author: '통증요정 김학조', youtubeId: 'N-V53dmXJ9M', url: 'https://www.youtube.com/shorts/N-V53dmXJ9M' },
    { id: 'yt-qB7oUZeiV98', type: 'video', title: '의자 하나로 고관절 움직임 따라 하기', author: '1분재활 | 물리치료사 우주쌤', youtubeId: 'qB7oUZeiV98', url: 'https://www.youtube.com/shorts/qB7oUZeiV98' },
    { id: 'yt-exQ8fzkiZjg', type: 'video', title: '간단한 하체 운동 · 의자를 이용한 틈새 운동', author: '더셈 플레이 THESEM PLAY', youtubeId: 'exQ8fzkiZjg', url: 'https://www.youtube.com/shorts/exQ8fzkiZjg' },
    { id: 'yt-uw1aUHnMfo8', type: 'video', wide: true, title: '10분! 서서 하는 전신 스트레칭', author: '필라테스하는 물리치료사,람PT', youtubeId: 'uw1aUHnMfo8', url: 'https://www.youtube.com/watch?v=uw1aUHnMfo8' },
    { id: 'yt-EF_jq5ZaZMI', type: 'video', wide: true, title: '서서 하는 8분 복근운동 · 매트 없이 하는 홈트', author: '홈트마미', youtubeId: 'EF_jq5ZaZMI', url: 'https://www.youtube.com/watch?v=EF_jq5ZaZMI' },
    { id: 'yt-EN6IpNi45_E', type: 'video', wide: true, title: '태극권 30분 (30-Min Tai Chi)', author: 'Tai Chi for Health Daily', youtubeId: 'EN6IpNi45_E', url: 'https://www.youtube.com/watch?v=EN6IpNi45_E' },
    { id: 'basic', type: 'gif', title: '기본 · 일어나 움직이기 그림', author: '' }
  ]
};
