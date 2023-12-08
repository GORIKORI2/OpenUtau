using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Japanese C+V Phonemizer", "JA C+V", "GORIKORI",language:"JA")]
    public class JapaneseC_VPhonemizer : Phonemizer {
        static readonly string[] plainVowels = new string[] {"a","i","u","e","o","n","N"};
        static readonly string[] nonVowels = new string[]{"R","-","k","ky","g","gy",
                                                           "s","sh","z","j","t","ch","ty","ts",
                                                           "d","dy","n","ny","h","hy","f","b",
                                                           "by","p","py","m","my","y","r","4",
                                                           "ry","w","v","ng","l","・","B", "H",
        };

        static readonly string[] vowels = new string[] {
            "a=a,ka,kya,ga,gya,sa,sha,ta,tsa,tya,cha,za,ja,da,dya,ha,hya,fa,ba,bya,la,va,wa,na,nya,ya,ra,rya,ma,mya,pa,pya",
            "e=e,ke,kye,ge,gye,se,she,te,tse,tye,che,ze,je,de,dye,he,hye,fe,be,bye,le,ve,we,ne,nye,ye,re,rye,me,mye,pe,pye",
            "i=i,ki,kyi,gi,gyi,si,shi,ti,tsi,tyi,chi,ze,je,de,dye,hi,hyi,fi,bi,byi,li,vi,wi,ni,nyi,yi,ri,ryi,mi,myi,pi,pyi",
            "o=o,ko,kyo,go,gyo,so,sho,to,tso,tyo,cho,zo,jo,do,dyo,ho,hyo,fo,bo,byo,lo,vo,wo,no,nyo,yo,ro,ryo,mo,myo,po,pyo",
            "n=n",
            "u=u,ku,kyu,gu,gyu,su,shu,tu,tsu,tyu,chu,zu,ju,du,dyu,hu,hyu,fu,bu,byu,lu,vu,wu,nu,nyu,yu,ru,ryu,mu,myu,pu,pyu",
            "N=ng",
            "・=・",
        };

        static readonly string[] consonants = new string[] {
            "ch=chi,che,cha,chu,cho",
            "gy=gye,gya,gyu,gyo,gyi",
            "ts=tsu,tsa,tsi,tse,tso",
            "ty=tyi,tye,tya,tyu,tyo",
            "py=pyi,pye,pya,pyu,pyo",
            "ry=ryi,rye,ryu,rya,ryo",
            "ny=nyi,nye,nya,nyu,nyo",
            "r=ra,ru,ri,re,ro",
            "hy=hyi,hye,hya,hyu,hyo",
            "dy=dyi,dye,dyu,dya,dyo",
            "by=byi,bye,bya,byu,byo",
            "b=ba,bi,bu,be,bo",
            "d=だ,で,ど,どぃ,どぅ",
            "g=が,ぐ,ぐぃ,げ,ご",
            "f=ふ,ふぁ,ふぃ,ふぇ,ふぉ",
            "h=は,はぃ,へ,ほ,ほぅ",
            "k=か,く,くぃ,け,こ",
            "j=じ,じぇ,じゃ,じゅ,じょ,ぢ,ぢぇ,ぢゃ,ぢゅ,ぢょ",
            "m=ま,む,むぃ,め,も",
            "n=な,ぬ,ぬぃ,ね,の",
            "p=ぱ,ぷ,ぷぃ,ぺ,ぽ",
            "s=さ,す,すぃ,せ,そ",
            "sh=し,しぇ,しゃ,しゅ,しょ",
            "t=た,て,と,とぃ,とぅ",
            "v=ヴ,ヴぁ,ヴぃ,ヴぅ,ヴぇ,ヴぉ",
            "ky=き,きぇ,きゃ,きゅ,きょ",
            "w=うぃ,うぅ,うぇ,うぉ,わ,ゐ,ゑ,を,ヰ,ヱ",
            "y=いぃ,いぇ,や,ゆ,よ",
            "z=ざ,ず,ずぃ,ぜ,ぞ",
            "my=み,みぇ,みゃ,みゅ,みょ",
            "l=la,li,lu,le,lo",
            "・=・a,・i,・u,・e,・o,・n,・ng",
        };

        // in case voicebank is missing certain symbols
        static readonly string[] substitution = new string[] {  
            "ty,ch,ts=t", "j,dy=d", "gy=g", "ky=k", "py=p", "ny=n", "ry=r", "my=m", "hy,f=h", "by,v=b", "dz=z", "l=r", "ly=l"
        };

        static readonly Dictionary<string, string> vowelLookup;
        static readonly Dictionary<string, string> consonantLookup;
        static readonly Dictionary<string, string> substituteLookup;

        static JapaneseCVVCPhonemizer() {
            vowelLookup = vowels.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            consonantLookup = consonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            substituteLookup = substitution.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[0].Split(',').Select(orig => (orig, parts[1]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        // Store singer in field, will try reading presamp.ini later
        private USinger singer;
        public override void SetSinger(USinger singer) => this.singer = singer;

        // make it quicker to check multiple oto occurrences at once rather than spamming if else if
        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            var attr1 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + attr.alternate, note.tone + attr.toneShift, attr.voiceColor, out var otoAlt)) {
                    otos.Add(otoAlt);
                } else if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            string color = attr.voiceColor ?? "";
            if (otos.Count > 0) {
                if (otos.Any(oto => (oto.Color ?? string.Empty) == color)) {
                    oto = otos.Find(oto => (oto.Color ?? string.Empty) == color);
                    return true;
                } else if (otos.Any(oto => (color ?? string.Empty) == color)) {
                    oto = otos.Find(oto => (color ?? string.Empty) == color);
                    return true;
                } else {
                    return false;
                }
            }
            return false;
        }

        // checking VCs
        // when VC does not exist, it will not be inserted
        private bool checkOtoUntilHitVc(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + attr.alternate, note.tone + attr.toneShift, attr.voiceColor, out var otoAlt)) {
                    otos.Add(otoAlt);
                } else if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            string color = attr.voiceColor ?? "";
            if (otos.Count > 0) {
                if (otos.Any(oto => (oto.Color ?? string.Empty) == color)) {
                    oto = otos.Find(oto => (oto.Color ?? string.Empty) == color);
                    return true;
                } else {
                    return false;
                }
            }
            return false;
        }


        // can probably be cleaned up more but i have work in the morning. have fun.
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentLyric = note.lyric.Normalize();
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                currentLyric = note.phoneticHint.Normalize();
            }
            var originalCurrentLyric = currentLyric;
            var cfLyric = $"* {currentLyric}";
            var attr0 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            var attr1 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;

            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                string[] tests = new string[] { currentLyric };
                // Not convert VCV
                if (checkOtoUntilHit(tests, note, out var oto)) {
                    currentLyric = oto.Alias;
                }
            } else if (prevNeighbour == null) {
                // Use "- V" or "- CV" if present in voicebank
                var initial = $"- {currentLyric}";
                string[] tests = new string[] { initial, currentLyric };
                // try [- XX] before trying plain lyric
                if (checkOtoUntilHit(tests, note, out var oto)) {
                    currentLyric = oto.Alias;
                }
            } else if (plainVowels.Contains(currentLyric) || nonVowels.Contains(currentLyric)) {
                var prevLyric = prevNeighbour.Value.lyric.Normalize();
                if (!string.IsNullOrEmpty(prevNeighbour.Value.phoneticHint)) {
                    prevLyric = prevNeighbour.Value.phoneticHint.Normalize();
                }
                // Current note is VV
                if (vowelLookup.TryGetValue(prevLyric.LastOrDefault().ToString() ?? string.Empty, out var vow)) {
                    var vowLyric = $"{vow} {currentLyric}";
                    // try vowlyric before cflyric, if both fail try currentlyric
                    string[] tests = new string[] {vowLyric, cfLyric, currentLyric};
                    if (checkOtoUntilHit(tests, note, out var oto)){
                        currentLyric = oto.Alias;
                    }
                }
            } else {
                string[] tests = new string[] {cfLyric, currentLyric};
                if (checkOtoUntilHit(tests, note, out var oto)){
                    currentLyric = oto.Alias;
                }
            }

            if (nextNeighbour != null && string.IsNullOrEmpty(nextNeighbour.Value.phoneticHint)) {
                var nextLyric = nextNeighbour.Value.lyric.Normalize();

                // Check if next note is a vowel and does not require VC
                if (nextLyric.Length == 1 && plainVowels.Contains(nextLyric)) {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = currentLyric,
                            }
                        },
                    };
                }

                // Insert VC before next neighbor
                // Get vowel from current note
                var vowel = "";
                if (vowelLookup.TryGetValue(originalCurrentLyric.LastOrDefault().ToString() ?? string.Empty, out var vow)) {
                    vowel = vow;
                }

                // Get consonant from next note
                var consonant = "";
                if (consonantLookup.TryGetValue(nextLyric.FirstOrDefault().ToString() ?? string.Empty, out var con) || (nextLyric.Length >= 2 && consonantLookup.TryGetValue(nextLyric.Substring(0, 2), out con))) {
                    consonant = con;
                }


                if (consonant == "") {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = currentLyric,
                            }
                        },
                    };
                }

                var vcPhoneme = $"{vowel} {consonant}";
                var vcPhonemes = new string[] {vcPhoneme, ""};
                // find potential substitute symbol
                if (substituteLookup.TryGetValue(consonant ?? string.Empty, out con)){
                        vcPhonemes[1] = $"{vowel} {con}";
                }
                //if (singer.TryGetMappedOto(vcPhoneme, note.tone + attr0.toneShift, attr0.voiceColor, out var oto1)) {
                if (checkOtoUntilHitVc(vcPhonemes, note, out var oto1)) {
                    vcPhoneme = oto1.Alias;
                } else {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = currentLyric,
                            }
                        },
                    };
                }

                int totalDuration = notes.Sum(n => n.duration);
                int vcLength = 120;
                var nextAttr = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                if (singer.TryGetMappedOto(nextLyric, nextNeighbour.Value.tone + nextAttr.toneShift, nextAttr.voiceColor, out var oto)) {
                    // If overlap is a negative value, vcLength is longer than Preutter
                    if (oto.Overlap < 0) {
                        vcLength = MsToTick(oto.Preutter - oto.Overlap);
                    } else {
                        vcLength = MsToTick(oto.Preutter);
                    }
                }
                // vcLength depends on the Vel of the next note
                vcLength = Convert.ToInt32(Math.Min(totalDuration / 2, vcLength * (nextAttr.consonantStretchRatio ?? 1)));

                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = currentLyric,
                        },
                        new Phoneme() {
                            phoneme = vcPhoneme,
                            position = totalDuration - vcLength,
                        }
                    },
                };
            }

            // No next neighbor
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = currentLyric,
                    }
                },
            };
        }
    }
}
