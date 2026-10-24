import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MockOption } from '../option';

@Component({
  selector: 'app-sound-it-out-mock',
  imports: [MockOption],
  template: `
    <section class="mk-game">
      <h2>Sound It Out <span class="mk-chip">Phase 3</span></h2>
      <p class="mk-sub">
        Pictures that spell a name one syllable at a time. All three options show the same puzzle: cob, bee, brie,
        ant. Emoji stand in for the hand-picked images.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="sound-it-out"
          letter="A"
          name="Picture equation"
          bar="Sound It Out #9"
          plus="The whole puzzle is visible at once, and it reads naturally as first name plus last name."
          minus="Typing a name with only blanks to go on is hard on anyone unsure of the spelling."
        >
          <p class="mk-ask">Say what you see</p>
          <div class="eq"><span class="pic">🌽</span><i>+</i><span class="pic">🐝</span></div>
          <div class="eq"><span class="pic">🧀</span><i>+</i><span class="pic">🐜</span></div>
          <div class="blanks">
            @for (word of firstLetters; track $index) {
              <span>
                @for (l of word; track $index) {
                  <b>{{ l }}</b>
                }
              </span>
            }
          </div>
          <span class="mk-btn ghost">Reveal a letter (−10 pts)</span>
          <div class="mk-spacer"></div>
          <div class="mk-input"><span>Type the name</span><b>↵</b></div>
        </app-mock-option>

        <app-mock-option
          game="sound-it-out"
          letter="B"
          name="Letter bank"
          bar="Sound It Out #9"
          plus="Tapping letters from a bank removes the spelling problem and feels like a puzzle, not a quiz."
          minus="The bank narrows the answer a lot, so it is the easiest version; no keyboard, no autocomplete reuse."
        >
          <div class="grid">
            @for (p of pics; track p) {
              <span class="pic">{{ p }}</span>
            }
          </div>
          <div class="blanks">
            @for (word of typed; track $index) {
              <span>
                @for (l of word; track $index) {
                  <b>{{ l }}</b>
                }
              </span>
            }
          </div>
          <div class="bank">
            @for (l of bank; track $index) {
              <b [class.used]="l.used">{{ l.letter }}</b>
            }
          </div>
          <div class="mk-pair">
            <span class="mk-btn ghost">Shuffle</span>
            <span class="mk-btn ghost">Clear</span>
          </div>
        </app-mock-option>

        <app-mock-option
          game="sound-it-out"
          letter="C"
          name="One sound at a time"
          bar="Sound It Out #9"
          plus="Big images and a step for each syllable make hard rebuses approachable; hearing it helps too."
          minus="More taps to see everything, and audio clips are extra content to produce."
        >
          <div class="hints">
            @for (s of solved; track $index) {
              <span class="mk-chip" [class.on]="s !== '?'">{{ s }}</span>
            }
          </div>
          <div class="mk-card focus">
            <span class="mk-label">Picture 3 of 4</span>
            <span class="huge">🧀</span>
            <span class="word">_ _ _ _</span>
          </div>
          <div class="mk-pair">
            <span class="mk-btn ghost">🔊 Hear it</span>
            <span class="mk-btn ghost">Show word</span>
          </div>
          <div class="mk-meta"><span>‹ Previous</span><span>Next ›</span></div>
          <div class="mk-spacer"></div>
          <div class="mk-input"><span>Guess the name any time</span><b>↵</b></div>
        </app-mock-option>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .pic {
      width: 84px; height: 84px; border-radius: 14px; background: #fff;
      display: grid; place-items: center; font-size: 46px; line-height: 1;
    }
    .eq { display: flex; align-items: center; justify-content: center; gap: 12px; }
    .eq i { font-style: normal; font-family: var(--display); font-size: 26px; color: var(--muted); }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .grid .pic { width: auto; height: 104px; font-size: 56px; }

    .blanks { display: flex; justify-content: center; gap: 14px; }
    .blanks span { display: flex; gap: 4px; }
    .blanks b {
      width: 22px; height: 30px; border-bottom: 3px solid var(--line); display: grid; place-items: center;
      font-family: var(--display); font-size: 20px;
    }
    .bank { display: grid; grid-template-columns: repeat(7, 1fr); gap: 5px; }
    .bank b {
      aspect-ratio: 1; border-radius: 8px; background: var(--surface-2); border: 1px solid var(--line);
      display: grid; place-items: center; font-family: var(--display); font-size: 18px;
    }
    .bank .used { opacity: .25; }

    .hints { display: flex; flex-wrap: wrap; gap: 6px; }
    .focus { display: grid; justify-items: center; gap: 6px; padding: 16px; }
    .huge { font-size: 110px; line-height: 1.1; }
    .word { font-family: var(--display); font-size: 22px; letter-spacing: .2em; color: var(--muted); }
  `,
})
export class SoundItOutMock {
  protected readonly pics = ['🌽', '🐝', '🧀', '🐜'];
  protected readonly firstLetters = [['K', '', '', ''], ['B', '', '', '', '', '']];
  protected readonly typed = [['K', 'O', '', ''], ['', '', '', '', '', '']];
  protected readonly solved = ['cob', 'bee', '?', '?'];
  protected readonly bank = [...'TKSBAORENLYBIM'].map((letter, i) => ({ letter, used: i === 1 || i === 5 }));
}
