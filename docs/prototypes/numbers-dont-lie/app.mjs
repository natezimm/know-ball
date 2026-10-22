import { leagues, preferenceKey, readPreferences, dailySet, practiceSet, score } from './model.mjs';

const stage = document.querySelector('#stage');
const escape = text => String(text).replace(/[&<>"']/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' })[char]);
let storage;
try { storage = window.localStorage; } catch { /* Preferences still work for this visit. */ }
let selected = readPreferences(storage);
let questions, mode = 'daily', round = [], answers = [], index = 0, pending = false;
const number = value => value.toLocaleString('en-US');
const focusHeading = () => stage.querySelector('h2')?.focus();
const card = html => { stage.innerHTML = `<div class="card">${html}</div>`; };
const heading = text => `<h2 tabindex="-1">${escape(text)}</h2>`;

function switchMode(next) {
  if (round.length && answers.length && index < 5 && !confirm('Leave this unfinished preview round? Its answers will be reset.')) return;
  mode = next; round = []; answers = []; index = 0; pending = false;
  for (const name of ['daily', 'practice']) document.getElementById(name).setAttribute('aria-pressed', String(mode === name));
  setup(); focusHeading();
}

function setup() {
  if (mode === 'daily') {
    card(`<p class="meta">Shared daily · Sample edition</p>${heading('Same five questions. Everyone plays.')}<p>Pick one of four answers, then lock it in. See the numbers behind every answer, even when you miss.</p><p>One question from each league, plus an NBA bonus. No timer.</p><button class="primary" id="start">Play daily preview</button>`);
    stage.querySelector('#start').onclick = () => start(dailySet(questions));
    return;
  }
  card(`<p class="meta">Your leagues. Your practice.</p>${heading('Choose your leagues')}<p>Practice only changes this round. Everyone still gets the same daily quiz.</p><form id="setup"><fieldset><legend>Select one or more leagues</legend><button type="button" id="all">All leagues</button><div class="leagues">${leagues.map(l => `<label class="choice"><input type="checkbox" name="league" value="${l}" ${selected.includes(l) ? 'checked' : ''}>${l.toUpperCase()}</label>`).join('')}</div></fieldset><p id="selection" aria-live="polite"></p><p class="error" id="error" role="alert"></p><button class="primary">Start practice</button></form>`);
  const sync = () => {
    selected = [...stage.querySelectorAll('input:checked')].map(input => input.value);
    stage.querySelector('#selection').textContent = selected.length ? `Five questions · ${selected.map(l => l.toUpperCase()).join(' + ')}` : 'No leagues selected.';
    stage.querySelector('#error').textContent = '';
    try { storage?.setItem(preferenceKey, JSON.stringify(selected)); } catch { /* Best effort, never blocks play. */ }
  };
  stage.querySelectorAll('input').forEach(input => input.onchange = sync);
  stage.querySelector('#all').onclick = () => { stage.querySelectorAll('input').forEach(input => input.checked = true); sync(); };
  stage.querySelector('#setup').onsubmit = event => {
    event.preventDefault();
    try { start(practiceSet(questions, selected)); }
    catch (error) { stage.querySelector('#error').textContent = error.message; }
  };
  sync();
}

function start(set) { round = set; answers = []; index = 0; pending = false; renderQuestion(); focusHeading(); }

function evidence(q) {
  return `<p>${escape(q.explanation)}</p><ul class="values">${q.options.map(o => `<li><span>${escape(o.label)}</span><strong>${number(o.value)}</strong></li>`).join('')}</ul><p class="source">${escape(q.unit)} · Regular season only.</p><p class="source">Source: <a href="${escape(q.source.url)}" target="_blank" rel="noopener noreferrer">${escape(q.source.credit)}</a>${q.source.licenseUrl ? ` · <a href="${q.source.licenseUrl}" target="_blank" rel="noopener noreferrer">CC BY 4.0</a> · Weekly totals aggregated by Know Ball; no endorsement.` : ''}</p>`;
}

function renderQuestion() {
  const q = round[index], answer = answers[index];
  const correct = q.options.find(o => o.id === q.correctOptionId);
  card(`<div class="meta"><span>${mode === 'daily' ? 'Daily preview' : 'Practice'} · ${q.league.toUpperCase()}</span><span>${escape(q.season)} · Regular season</span></div><div class="progress" aria-label="${answers.length} of 5 answered">${Array.from({length:5}, (_,i) => `<span class="${i < answers.length ? 'done' : ''}"></span>`).join('')}</div><p class="meta">Question ${index + 1} of 5</p>${heading(q.prompt)}<form id="answer"><fieldset ${answer ? 'disabled' : ''}><legend>Choose one answer</legend><div class="choices">${q.options.map(o => `<label class="choice ${answer && o.id === q.correctOptionId ? 'correct' : ''}"><input type="radio" name="answer" value="${o.id}" ${answer?.optionId === o.id ? 'checked' : ''}><span>${escape(o.label)}</span>${answer && o.id === q.correctOptionId ? '<span class="tag">Correct</span>' : ''}</label>`).join('')}</div></fieldset>${answer ? '' : '<button class="primary" id="lock" disabled>Lock in answer</button>'}</form>${answer ? `<section class="feedback ${answer.optionId !== q.correctOptionId ? 'missed' : ''}" aria-label="Answer explanation"><h3 tabindex="-1" id="feedback">${answer.optionId === q.correctOptionId ? 'Correct!' : 'Not quite.'} ${escape(correct.label)}.</h3>${evidence(q)}<button class="primary" id="next">${index === 4 ? 'See results' : 'Next question'}</button></section>` : ''}`);
  if (answer) {
    stage.querySelector('#next').onclick = () => { index++; pending = false; if (index === 5) renderResult(); else renderQuestion(); focusHeading(); };
    return;
  }
  stage.querySelectorAll('input').forEach(input => input.onchange = () => stage.querySelector('#lock').disabled = false);
  stage.querySelector('#answer').onsubmit = event => {
    event.preventDefault();
    const optionId = new FormData(event.currentTarget).get('answer');
    if (pending || !q.options.some(o => o.id === optionId)) return;
    pending = true; answers.push({ question:q, optionId }); renderQuestion(); stage.querySelector('#feedback').focus();
  };
}

function renderResult() {
  card(`<p class="meta">${mode === 'daily' ? 'Daily preview' : `Practice · ${selected.map(l => l.toUpperCase()).join(' + ')}`} completed</p>${heading('That’s the final score.')}<p class="score">${score(answers)} / 5</p><p>${answers.map((a,i) => `${i + 1}: ${a.optionId === a.question.correctOptionId ? 'Correct' : 'Missed'}`).join(' · ')}</p><p>Every number has a story. Review yours below.</p>${answers.map((a,i) => `<details><summary>Question ${i + 1} · ${a.question.league.toUpperCase()} · ${a.optionId === a.question.correctOptionId ? 'Correct' : 'Missed'}</summary><p>${escape(a.question.prompt)}</p><p>Your answer: ${escape(a.question.options.find(o => o.id === a.optionId).label)}</p>${evidence(a.question)}</details>`).join('')}<button id="again" class="primary">Choose practice leagues</button>`);
  stage.querySelector('#again').onclick = () => switchMode('practice');
}

try {
  const response = await fetch('./questions.json');
  if (!response.ok) throw new Error('Unable to load the sample questions.');
  questions = (await response.json()).questions;
  document.querySelector('#daily').onclick = () => switchMode('daily');
  document.querySelector('#practice').onclick = () => switchMode('practice');
  setup();
} catch {
  card(`${heading('The preview couldn’t load.')}<p>Serve the repository over HTTP using the instructions in the prototype README, then reload.</p><button id="retry">Reload preview</button>`);
  stage.querySelector('#retry').onclick = () => location.reload();
}
