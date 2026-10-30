// k6 load test: anonymous voting on a public poll.
//   k6 run -e BASE=http://localhost:8080 -e QUIZ_ID=1 -e TOKEN=<creator-jwt> loadtest/vote.js
//
// Notes
// - With one source IP, duplicate prevention (one anonymous response per IP) and the per-IP rate limits kick in
//   quickly. For realistic numbers run with Captcha disabled and send distinct X-Forwarded-For values
//   (needs Security__TrustForwardedHeaders=true on the API), or vote as many registered users (setup() below).
import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE || 'http://localhost:8080';
const QUIZ_ID = __ENV.QUIZ_ID || '1';

export const options = {
  scenarios: {
    voters: { executor: 'ramping-arrival-rate', startRate: 10, timeUnit: '1s', preAllocatedVUs: 200, maxVUs: 1000,
      stages: [{ target: 100, duration: '30s' }, { target: 300, duration: '60s' }, { target: 0, duration: '10s' }] },
  },
  thresholds: { http_req_failed: ['rate<0.05'], http_req_duration: ['p(95)<500'] },
};

function fakeIp() {
  return `10.${Math.floor(Math.random() * 255)}.${Math.floor(Math.random() * 255)}.${Math.floor(Math.random() * 254) + 1}`;
}

export default function () {
  const headers = { 'Content-Type': 'application/json', 'X-Forwarded-For': fakeIp() };

  const quiz = http.get(`${BASE}/api/quizzes/${QUIZ_ID}`, { headers });
  if (!check(quiz, { 'quiz loaded': (r) => r.status === 200 })) return;
  const questions = quiz.json('questions');

  const start = http.post(`${BASE}/api/quizzes/${QUIZ_ID}/start`, null, { headers });
  if (!check(start, { 'started': (r) => r.status === 200 })) return;
  headers['X-Session-Token'] = start.json('sessionToken');

  const answers = questions.map((q) => {
    if (q.type === 'rating') return { questionId: q.id, ratingValue: 1 + Math.floor(Math.random() * q.maxRating) };
    if (q.type === 'text') return { questionId: q.id, textAnswer: 'load test' };
    const o = q.options[Math.floor(Math.random() * q.options.length)];
    return { questionId: q.id, selectedOptionIds: [o.id] };
  });

  const res = http.post(`${BASE}/api/quizzes/${QUIZ_ID}/responses`, JSON.stringify(answers), { headers });
  check(res, { 'submitted': (r) => r.status === 200 });
}
