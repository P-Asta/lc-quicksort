using System.Collections.Generic;
using UnityEngine;
using GameNetcodeStuff;

namespace QuickSort
{
    public static class Extensions
    {
        // Compatibility: allow localized (e.g. Korean patch) item names to resolve to the canonical internal keys.
        // We normalize BOTH the input and the alias table in the same way (lowercase, spaces/hyphens -> underscores).
        private static readonly Dictionary<string, string> NameAliases = BuildNameAliases();

        private static string NormalizeKeyNoAlias(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return s.ToLower().Replace(" ", "_").Replace("-", "_").Trim();
        }

        private static Dictionary<string, string> BuildNameAliases()
        {
            var d = new Dictionary<string, string>();

            void Add(string localized, string canonical)
            {
                string k = NormalizeKeyNoAlias(localized);
                string v = NormalizeKeyNoAlias(canonical);
                if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v)) return;

                // If duplicates exist (some Korean patch strings are reused across different mods),
                // prefer the first mapping we add (vanilla first, then modded).
                if (!d.ContainsKey(k))
                    d.Add(k, v);
            }

            void AddAliases(string canonicalKey, params string[] aliases)
            {
                // Ensure the canonical key resolves to itself.
                Add(canonicalKey, canonicalKey);
                if (aliases == null) return;
                foreach (string a in aliases)
                    Add(a, canonicalKey);
            }

            // --- Korean patch mappings ---
            // The LCKorean patch swaps Item.itemName between Korean and English (and sometimes uses
            // non-vanilla English labels like "Mug", "Brush", "Bell", "Hive", etc.).
            // We map BOTH Korean and these English variants to the same canonical key.

            // Core store/scrap items used in configs (skippedItems) and common commands
            var core = new (string canonical, string[] aliases)[]
            {
                ("boombox", new[] { "붐박스" }),
                ("flashlight", new[] { "손전등" }),
                ("jetpack", new[] { "제트팩" }),
                ("key", new[] { "열쇠" }),
                ("lockpicker", new[] { "자물쇠 따개" }),
                ("apparatus", new[] { "장치", "apparatice" }),
                ("pro_flashlight", new[] { "프로 손전등", "pro-flashlight" }),
                ("shovel", new[] { "철제 삽" }),
                ("stun_grenade", new[] { "기절 수류탄", "stun grenade" }),
                ("extension_ladder", new[] { "연장형 사다리", "extension ladder" }),
                ("tzp_inhalant", new[] { "tzp-흡입제", "tzp-inhalant" }),
                ("walkie_talkie", new[] { "무전기", "walkie-talkie" }),
                ("zap_gun", new[] { "잽건", "zap gun" }),
                ("radar_booster", new[] { "레이더 부스터", "radar booster", "radar-booster" }),
                ("spray_paint", new[] { "페인트 스프레이", "스프레이 페인트", "spray paint" }),
                ("shotgun", new[] { "산탄총" }),
                ("ammo", new[] { "탄약" }),
                ("clipboard", new[] { "클립보드" }),
                ("sticky_note", new[] { "스티커 메모", "스티커메모", "sticky note" }),
            };
            foreach (var group in core)
                AddAliases(group.canonical, group.aliases);

            // Variants used by LCKorean patch that don't match vanilla keys
            var variants = new (string canonical, string[] aliases)[]
            {
                ("coffee_mug", new[] { "mug", "머그잔", "coffee mug", "커피 머그잔" }),
                ("hair_brush", new[] { "brush", "hair brush", "빗" }),
                ("brass_bell", new[] { "bell", "brass bell", "황동 종", "종" }),
                ("bee_hive", new[] { "hive", "bee hive", "벌집" }),
                ("wedding_ring", new[] { "ring", "wedding ring", "반지" }),
                ("robot_toy", new[] { "toy robot", "robot toy", "장난감 로봇", "로봇 장난감" }),
                ("rubber_ducky", new[] { "rubber ducky", "고무 오리" }),
                ("tattered_metal_sheet", new[] { "metal sheet", "tattered metal sheet", "금속 판", "너덜너덜한 금속 판" }),
                ("homemade_flashbang", new[] { "homemade flashbang", "사제 섬광탄" }),
            };
            foreach (var group in variants)
                AddAliases(group.canonical, group.aliases);

            // Extra modded items seen in LCKorean patch (UntranslateModdedItem)
            var modded = new (string canonical, string[] aliases)[]
            {
                ("bubblegun", new[] { "비눗방울 총" }),
                ("broken_p88", new[] { "망가진 p88", "broken p88" }),
                ("employee", new[] { "직원" }),
                ("mine", new[] { "지뢰" }),
                ("toothles", new[] { "투슬리스", "toothles" }),
                ("crossbow", new[] { "석궁", "crossbow" }),
                ("physgun", new[] { "피직스건", "physgun" }),
                ("ammo_crate", new[] { "탄약 상자", "ammo crate" }),
                ("drink", new[] { "음료수" }),
                ("radio", new[] { "라디오" }),
                ("mouse", new[] { "마우스" }),
                ("monitor", new[] { "모니터" }),
                ("battery", new[] { "건전지" }),
                ("cannon", new[] { "대포" }),
                ("health_drink", new[] { "건강 음료", "health drink" }),
                ("chemical", new[] { "화학 약품" }),
                ("disinfecting_alcohol", new[] { "소독용 알코올", "disinfecting alcohol" }),
                ("ampoule", new[] { "앰풀" }),
                ("blood_pack", new[] { "혈액 팩", "blood pack" }),
                ("flip_lighter", new[] { "라이터", "flip lighter" }),
                ("rubber_ball", new[] { "고무 공", "rubber ball" }),
                ("video_tape", new[] { "비디오 테이프", "video tape" }),
                ("first_aid_kit", new[] { "구급 상자", "first aid kit" }),
                ("gold_medallion", new[] { "금메달", "gold medallion" }),
                ("steel_pipe", new[] { "금속 파이프", "steel pipe" }),
                ("axe", new[] { "도끼" }),
                ("emergency_hammer", new[] { "비상용 망치", "emergency hammer" }),
                ("katana", new[] { "카타나" }),
                ("silver_medallion", new[] { "은메달", "silver medallion" }),
                ("pocket_radio", new[] { "휴대용 라디오", "pocket radio" }),
                ("teddy_plush", new[] { "곰 인형", "teddy plush" }),
            };
            foreach (var group in modded)
                AddAliases(group.canonical, group.aliases);

            // Vanilla items (ItemScanNode(oldHeader, newHeader))
            Add("마법의 7번 공", "Magic 7 ball");
            Add("에어혼", "Airhorn");
            Add("황동 종", "Brass bell");
            Add("큰 나사", "Big bolt");
            Add("병 묶음", "Bottles");
            Add("빗", "Hair brush");
            Add("사탕", "Candy");
            Add("금전 등록기", "Cash register");
            Add("화학 용기", "Chemical jug");
            Add("광대 나팔", "Clown horn");
            Add("대형 축", "Large axle");
            Add("틀니", "Teeth");
            Add("쓰레받기", "Dust pan");
            Add("달걀 거품기", "Egg beater");
            Add("v형 엔진", "V-type engine");
            Add("황금 컵", "Golden cup");
            Add("멋진 램프", "Fancy lamp");
            Add("그림", "Painting");
            Add("플라스틱 물고기", "Plastic fish");
            Add("레이저 포인터", "Laser pointer");
            Add("금 주괴", "Gold Bar");
            Add("헤어 드라이기", "Hairdryer");
            Add("돋보기", "Magnifying glass");
            Add("너덜너덜한 금속 판", "Tattered metal sheet");
            Add("쿠키 틀", "Cookie mold pan");
            Add("머그잔", "Coffee mug");
            Add("커피 머그잔", "Coffee mug");
            Add("향수 병", "Perfume bottle");
            Add("구식 전화기", "Old phone");
            Add("피클 병", "Jar of pickles");
            Add("약 병", "Pill bottle");
            Add("리모컨", "Remote");
            Add("결혼 반지", "Wedding ring");
            Add("로봇 장난감", "Robot Toy");
            Add("고무 오리", "Rubber ducky");
            Add("빨간색 소다", "Red soda");
            Add("운전대", "Steering wheel");
            Add("정지 표지판", "Stop sign");
            Add("찻주전자", "Tea Kettle");
            Add("치약", "Toothpaste");
            Add("장난감 큐브", "Toy cube");
            Add("벌집", "Bee hive");
            Add("양보 표지판", "Yield sign");
            Add("산탄총", "Shotgun");
            Add("더블 배럴", "Double-barrel");
            Add("산탄총 탄약", "Shotgun shell");
            Add("사제 섬광탄", "Homemade Flashbang");
            Add("선물", "Gift");
            Add("선물 상자", "Gift box");
            Add("플라스크", "Flask");
            Add("비극", "Tragedy");
            Add("희극", "Comedy");
            Add("방귀 쿠션", "Whoopie cushion");
            Add("방퀴 쿠션", "Whoopie cushion"); // legacy typo fallback
            Add("식칼", "Kitchen knife");
            Add("부활절 달걀", "Easter egg");
            Add("제초제", "Weed killer");
            Add("벨트 배낭", "Belt bag");
            Add("축구공", "Soccer ball");
            Add("조작 패드", "Control pad");
            Add("쓰레기통 뚜껑", "Garbage lid");
            Add("플라스틱 컵", "Plastic cup");
            Add("화장실 휴지", "Toilet paper");
            Add("장난감 기차", "Toy train");
            Add("제드 도그", "Zed Dog");
            Add("시계", "Clock");
            Add("시체", "Body");
            Add("알", "Egg");

            // Additional vanilla objects/props translated directly (not via ItemScanNode in the snippet)
            Add("열쇠", "Key");
            Add("데이터 칩", "Data chip");
            Add("교육용 지침서", "Training manual");
            Add("장치", "Apparatus");
            Add("장치", "Apparatice"); // legacy typo seen in some patch code; kept as fallback

            // Modded content (TranslateModdedContent)
            // ImmersiveScraps
            Add("알코올 플라스크", "Alcohol Flask");
            Add("모루", "Anvil");
            Add("야구 방망이", "Baseball bat");
            Add("맥주 캔", "Beer can");
            Add("벽돌", "Brick");
            Add("망가진 엔진", "Broken engine");
            Add("양동이", "Bucket");
            Add("페인트 캔", "Can paint");
            Add("수통", "Canteen");
            Add("자동차 배터리", "Car battery");
            Add("조임틀", "Clamp");
            Add("멋진 그림", "Fancy Painting");
            Add("선풍기", "Fan");
            Add("소방 도끼", "Fireaxe");
            Add("소화기", "Fire extinguisher");
            Add("소화전", "Fire hydrant");
            Add("통조림", "Food can");
            Add("게임보이", "Gameboy");
            Add("쓰레기", "Garbage");
            Add("망치", "Hammer");
            Add("기름통", "Jerrycan");
            Add("키보드", "Keyboard");
            Add("랜턴", "Lantern");
            Add("도서관 램프", "Library lamp");
            Add("식물", "Plant");
            Add("플라이어", "Pliers");
            Add("뚫어뻥", "Plunger");
            Add("레트로 장난감", "Retro Toy");
            Add("스크류 드라이버", "Screwdriver");
            Add("싱크대", "Sink");
            Add("소켓 렌치", "Socket Wrench");
            // "Squeaky toy" is mapped to "고무 오리" in the snippet, but that conflicts with vanilla Rubber ducky.
            // We intentionally keep the vanilla mapping for "고무 오리" by adding vanilla first.
            Add("여행 가방", "Suitcase");
            Add("토스터기", "Toaster");
            Add("공구 상자", "Toolbox");
            Add("실크햇", "Top hat");
            Add("라바콘", "Traffic cone");
            Add("환풍구", "Vent");
            Add("물뿌리개", "Watering Can");
            Add("바퀴", "Wheel");
            Add("와인 병", "Wine bottle");
            Add("렌치", "Wrench");

            // Wesleys (as seen in the snippet)
            Add("자수정 군집", "Amethyst Cluster");
            Add("주사기", "Syringe");
            Add("주사기총", "Syringe Gun");
            Add("코너 파이프", "Corner Pipe");
            Add("작은 파이프", "Small Pipe");
            Add("파이프", "Flow Pipe");
            Add("뇌가 담긴 병", "Brain Jar");
            Add("호두까기 인형 장난감", "Toy Nutcracker");
            Add("시험관", "Test Tube");
            Add("시험관 랙", "Test Tube Rack");
            Add("호두까기 인형 눈", "Nutcracker Eye");
            Add("파란색 시험관", "Blue Test Tube");
            Add("노란색 시험관", "Yellow Test Tube");
            Add("빨간색 시험관", "Red Test Tube");
            Add("초록색 시험관", "Green Test Tube");
            Add("쇠지렛대", "Crowbar");
            Add("플젠", "Plzen");
            Add("컵", "Cup");
            Add("전자레인지", "Microwave");
            Add("hyper acid 실험 기록", "Experiment Log Hyper Acid");
            Add("희극 가면 실험 기록", "Experiment Log Comedy Mask");
            Add("저주받은 동전 실험 기록", "Experiment Log Cursed Coin");
            Add("바이오 hxnv7 실험 기록", "Experiment Log BIO HXNV7");
            Add("파란색 폴더", "Blue Folder");
            Add("빨간색 폴더", "Red Folder");
            Add("코일", "Coil");
            Add("타자기", "Typewriter");
            Add("서류 더미", "Documents");
            Add("스테이플러", "Stapler");
            Add("구식 컴퓨터", "Old Computer");
            Add("브론즈 트로피", "Bronze Trophy");
            Add("바나나", "Banana");
            Add("스턴봉", "Stun Baton");
            Add("바이오-hxnv7", "BIO-HXNV7");
            Add("복구된 비밀 일지", "Recovered Secret Log");
            Add("황금 단검 실험 기록", "Experiment Log Golden Dagger");
            Add("대합", "Clam");
            Add("거북이 등딱지", "Turtle Shell");
            Add("생선 뼈", "Fish Bones");
            Add("뿔 달린 껍질", "Horned Shell");
            Add("도자기 찻잔", "Porcelain Teacup");
            Add("대리석", "Marble");
            Add("도자기 병", "Porcelain Bottle");
            Add("도자기 향수 병", "Porcelain Perfume Bottle");
            Add("발광구", "Glowing Orb");
            Add("황금 해골", "Golden Skull");
            Add("코스모코스 지도", "Map of Cosmocos");
            Add("젖은 노트 1", "Wet Note 1");
            Add("젖은 노트 2", "Wet Note 2");
            Add("젖은 노트 3", "Wet Note 3");
            Add("젖은 노트 4", "Wet Note 4");
            Add("우주빛 파편", "Cosmic Shard");
            Add("우주 생장물", "Cosmic Growth");
            Add("천상의 두뇌 덩어리", "Chunk of Celestial Brain");
            Add("파편이 든 양동이", "Bucket of Shards");
            Add("우주빛 손전등", "Cosmic Flashlight");
            Add("잊혀진 일지 1", "Forgotten Log 1");
            Add("잊혀진 일지 2", "Forgotten Log 2");
            Add("잊혀진 일지 3", "Forgotten Log 3");
            Add("안경", "Glasses");
            Add("생장한 배양 접시", "Grown Petri Dish");
            Add("배양 접시", "Petri Dish");
            Add("코스모채드", "Cosmochad");
            Add("죽어가는 우주빛 손전등", "Dying Cosmic Flashlight");
            Add("죽어가는 우주 생장물", "Dying Cosmic Growth");
            Add("혈액 배양 접시", "Blood Petri Dish");
            Add("악마 코스모채드", "Evil Cosmochad");
            Add("악마 코스모", "Evil Cosmo");
            Add("릴 코스모", "Lil Cosmo");
            Add("죽어가는 생장물 배양 접시", "Dying Grown Petri Dish");
            Add("감시하는 배양 접시", "Watching Petri Dish");
            Add("현미경", "Microscope");
            Add("원통형 바일", "Round Vile");
            Add("사각형 바일", "Square Vile");
            Add("타원형 바일", "Oval Vile");
            Add("해링턴 일지 1", "Harrington Log 1");
            Add("해링턴 일지 2", "Harrington Log 2");
            Add("해링턴 일지 3", "Harrington Log 3");
            Add("해링턴 일지 4", "Harrington Log 4");
            Add("생장물이 든 병", "Jar of Growth");
            Add("테이프 플레이어 일지 1", "Tape Player Log 1");
            Add("테이프 플레이어 일지 2", "Tape Player Log 2");
            Add("테이프 플레이어 일지 3", "Tape Player Log 3");
            Add("테이프 플레이어 일지 4", "Tape Player Log 4");
            Add("쇼핑 카트", "Shopping Cart");

            return d;
        }

        public static string NormalizeName(string s)
        {
            string n = NormalizeKeyNoAlias(s);
            return NameAliases.TryGetValue(n, out string mapped) ? mapped : n;
        }

        public static string Name(this GrabbableObject item)
        {
            return NormalizeName(item.itemProperties.itemName);
        }

        public static string Name(this Item item)
        {
            return NormalizeName(item.itemName);
        }
    }
}

