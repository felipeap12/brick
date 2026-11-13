using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Brick Breaker 2D completo em um único script.
/// Tudo (sprites, partículas, fundo, HUD) é gerado por código.
/// USO: crie um GameObject vazio na cena e adicione este script. Aperte Play.
/// Controles: mouse / toque / setas / A-D. Clique ou Espaço lança a bola.
/// </summary>
public class BrickBreaker : MonoBehaviour
{
    [Header("Ajustes")]
    public int startLives = 3;
    public float baseBallSpeed = 7f;
    public float paddleWidth = 1.7f;

    // ---------- Constantes do campo ----------
    const float HalfW = 4.6f;      // metade da largura do campo
    const float Top = 5.0f;        // parede superior
    const float KillY = -6.2f;     // abaixo disso perde vida
    const float PaddleY = -5.0f;
    const float PaddleH = 0.3f;
    const float BallR = 0.14f;
    const float BW = 0.88f, BH = 0.33f;
    const int Cols = 9;

    enum State { Ready, Playing, LevelClear, GameOver }

    class Brick
    {
        public Transform t;
        public SpriteRenderer sr, glow;
        public int hp, maxHp;
        public Color baseCol;
        public Vector2 pos;
        public float flash, spawn;
        public bool alive = true;
    }

    // ---------- Estado ----------
    State state;
    int score, lives, level, combo, bricksLeft, bricksBroken;
    float stateTimer, shake, paddleX, targetX, paddlePunch, lastPointerX;
    bool usePointer;
    Vector2 ballPos, ballVel;
    float ballSpeed;

    // ---------- Objetos ----------
    Camera cam;
    Transform paddle, ball;
    SpriteRenderer paddleSr, bgSr;
    TrailRenderer trail;
    ParticleSystem fx;
    readonly List<Brick> bricks = new List<Brick>();
    Transform[] stars; float[] starPhase, starSpeed;
    Sprite brickSprite, paddleSprite, circleSprite, glowSprite, whiteSprite, bgSprite;
    Texture2D circleTex;
    Material particleMat;

    // GUI
    GUIStyle sBig, sMid, sSmall;
    int lastFont;

    // =====================================================================
    //  INICIALIZAÇÃO
    // =====================================================================
    void Start()
    {
        Application.targetFrameRate = 60;
        BuildTextures();
        SetupCamera();
        BuildBackground();
        BuildWalls();
        BuildPaddle();
        BuildBall();
        BuildFx();
        StartGame();
    }

    void StartGame()
    {
        score = 0; lives = startLives; level = 1; combo = 0; bricksBroken = 0;
        BuildLevel();
        ResetBall();
    }

    // ---------- Texturas / sprites procedurais ----------
    Texture2D MakeTex(int w, int h, System.Func<int, int, Color> f)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f(x, y);
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    Texture2D RoundedRectTex(int w, int h, float r)
    {
        return MakeTex(w, h, (x, y) =>
        {
            float px = x + 0.5f - w / 2f, py = y + 0.5f - h / 2f;
            float qx = Mathf.Abs(px) - (w / 2f - r), qy = Mathf.Abs(py) - (h / 2f - r);
            float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude
                      + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            float a = Mathf.Clamp01(0.5f - d);
            float v = Mathf.Lerp(0.70f, 1f, y / (h - 1f));          // degradê vertical
            if (y > h * 0.62f) v += 0.10f;                           // brilho no topo
            if (d > -2.2f) v += 0.12f;                               // borda clara
            v = Mathf.Clamp01(v);
            return new Color(v, v, v, a);
        });
    }

    Sprite MakeSprite(Texture2D t, float ppu)
    {
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), ppu);
    }

    void BuildTextures()
    {
        brickSprite = MakeSprite(RoundedRectTex(128, 48, 12), 128f);   // 1 x 0.375
        paddleSprite = MakeSprite(RoundedRectTex(160, 30, 15), 100f);  // 1.6 x 0.3

        circleTex = MakeTex(64, 64, (x, y) =>
        {
            float dx = (x + 0.5f - 32f) / 32f, dy = (y + 0.5f - 32f) / 32f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01((1f - d) * 32f * 0.5f);
            float v = Mathf.Clamp01(1f - 0.25f * d);
            return new Color(v, v, v, a);
        });
        circleSprite = MakeSprite(circleTex, 64f);                      // 1 x 1

        glowSprite = MakeSprite(MakeTex(128, 128, (x, y) =>
        {
            float dx = (x + 0.5f - 64f) / 64f, dy = (y + 0.5f - 64f) / 64f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            return new Color(1, 1, 1, a * a);
        }), 64f);                                                       // 2 x 2

        whiteSprite = MakeSprite(MakeTex(4, 4, (x, y) => Color.white), 4f); // 1 x 1

        bgSprite = MakeSprite(MakeTex(4, 256, (x, y) =>
        {
            float t = y / 255f; // 0 = baixo, 1 = topo
            Color bottom = new Color(0.10f, 0.03f, 0.20f);
            Color top = new Color(0.02f, 0.04f, 0.14f);
            return Color.Lerp(bottom, top, t);
        }), 4f);                                                        // 1 x 64

        particleMat = new Material(Shader.Find("Sprites/Default"));
        particleMat.mainTexture = circleTex;
    }

    SpriteRenderer NewSprite(string name, Sprite s, Transform parent, Color c, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s; sr.color = c; sr.sortingOrder = order;
        return sr;
    }

    // ---------- Câmera / fundo / paredes ----------
    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var g = new GameObject("Main Camera");
            g.tag = "MainCamera";
            cam = g.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.08f);
        cam.transform.position = new Vector3(0, 0, -10);
        cam.transform.rotation = Quaternion.identity;
    }

    void FitCamera()
    {
        cam.orthographicSize = Mathf.Max(6.4f, (HalfW + 0.5f) / cam.aspect);
        float h = cam.orthographicSize * 2f, w = h * cam.aspect;
        var b = bgSr.sprite.bounds.size;
        bgSr.transform.localScale = new Vector3(w * 1.2f / b.x, h * 1.2f / b.y, 1);
    }

    void BuildBackground()
    {
        bgSr = NewSprite("Background", bgSprite, transform, Color.white, -100);
        FitCamera();

        int n = 70;
        stars = new Transform[n]; starPhase = new float[n]; starSpeed = new float[n];
        for (int i = 0; i < n; i++)
        {
            var sr = NewSprite("Star", circleSprite, transform, Color.white, -90);
            float s = Random.Range(0.03f, 0.10f);
            sr.transform.localScale = Vector3.one * s;
            sr.transform.position = new Vector3(Random.Range(-8f, 8f), Random.Range(-7f, 7f), 0);
            stars[i] = sr.transform;
            starPhase[i] = Random.value * 10f;
            starSpeed[i] = Random.Range(0.05f, 0.3f);
        }
    }

    void BuildWalls()
    {
        Color c = new Color(0.35f, 0.9f, 1f);
        float midY = (Top + KillY) / 2f, hgt = Top - KillY + 0.2f;
        MakeWall(new Vector2(-HalfW - 0.05f, midY), new Vector2(0.1f, hgt), c);
        MakeWall(new Vector2(HalfW + 0.05f, midY), new Vector2(0.1f, hgt), c);
        MakeWall(new Vector2(0, Top + 0.05f), new Vector2(HalfW * 2 + 0.2f, 0.1f), c);
    }

    void MakeWall(Vector2 pos, Vector2 size, Color c)
    {
        var core = NewSprite("Wall", whiteSprite, transform, new Color(c.r, c.g, c.b, 0.9f), -10);
        core.transform.position = pos; core.transform.localScale = size;
        var glow = NewSprite("WallGlow", whiteSprite, transform, new Color(c.r, c.g, c.b, 0.14f), -11);
        glow.transform.position = pos; glow.transform.localScale = size + new Vector2(0.35f, 0.35f);
    }

    // ---------- Raquete / bola / partículas ----------
    void BuildPaddle()
    {
        var go = new GameObject("Paddle");
        go.transform.SetParent(transform, false);
        paddle = go.transform;
        paddleSr = go.AddComponent<SpriteRenderer>();
        paddleSr.sprite = paddleSprite;
        paddleSr.color = new Color(0.45f, 0.92f, 1f);
        paddleSr.sortingOrder = 5;
        var glow = NewSprite("Glow", glowSprite, paddle, new Color(0.3f, 0.85f, 1f, 0.35f), 4);
        glow.transform.localScale = new Vector3(1.5f, 0.9f, 1);
    }

    void BuildBall()
    {
        var go = new GameObject("Ball");
        go.transform.SetParent(transform, false);
        ball = go.transform;

        var body = NewSprite("Body", circleSprite, ball, Color.white, 10);
        body.transform.localScale = Vector3.one * BallR * 2f;
        var glow = NewSprite("Glow", glowSprite, ball, new Color(0.6f, 0.95f, 1f, 0.5f), 9);
        glow.transform.localScale = Vector3.one * 1.1f;

        trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.3f;
        trail.widthMultiplier = BallR * 2f;
        trail.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
        trail.minVertexDistance = 0.02f;
        trail.numCapVertices = 4;
        trail.sortingOrder = 8;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.7f, 1f, 1f), 0), new GradientColorKey(new Color(0.4f, 0.5f, 1f), 1) },
            new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0f, 1) });
        trail.colorGradient = g;
    }

    void BuildFx()
    {
        var go = new GameObject("FX");
        go.transform.SetParent(transform, false);
        fx = go.AddComponent<ParticleSystem>();
        fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = fx.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.2f;
        main.maxParticles = 3000;
        main.startSpeed = 0f;
        main.startLifetime = 0.7f;
        var em = fx.emission; em.enabled = false;
        var sh = fx.shape; sh.enabled = false;

        var col = fx.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        col.color = g;
        var sz = fx.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = particleMat;
        r.sortingOrder = 20;
        fx.Play();
    }

    void Burst(Vector2 pos, Color c, int count, float speed)
    {
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            ep.position = new Vector3(pos.x, pos.y, 0);
            ep.velocity = Random.insideUnitCircle * Random.Range(speed * 0.3f, speed);
            ep.startColor = c;
            ep.startSize = Random.Range(0.07f, 0.2f);
            ep.startLifetime = Random.Range(0.4f, 0.9f);
            fx.Emit(ep, 1);
        }
    }

    // =====================================================================
    //  NÍVEIS
    // =====================================================================
    bool PatternHas(int pattern, int r, int c, int rows)
    {
        switch (pattern)
        {
            case 1: return (r + c) % 2 == 0;                              // xadrez
            case 2: return Mathf.Abs(c - 4) <= r + 1;                     // pirâmide invertida
            case 3: return !(r >= 2 && r <= rows - 2 && c >= 3 && c <= 5); // vazado no meio
            default: return true;                                          // cheio
        }
    }

    Color BrickDisplay(Brick b)
    {
        return Color.Lerp(b.baseCol, Color.white, 0.3f * (b.hp - 1));
    }

    void BuildLevel()
    {
        foreach (var b in bricks) if (b.alive && b.t) Destroy(b.t.gameObject);
        bricks.Clear();

        int rows = Mathf.Min(6 + (level - 1) / 2, 8);
        int pattern = (level - 1) % 4;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                if (!PatternHas(pattern, r, c, rows)) continue;

                var b = new Brick();
                b.pos = new Vector2((c - (Cols - 1) / 2f) * 1.0f, 4.4f - r * 0.45f);
                b.hp = 1 + (level >= 2 && r < 2 ? 1 : 0) + (level >= 4 && r == 0 ? 1 : 0);
                b.maxHp = b.hp;
                b.baseCol = Color.HSVToRGB(Mathf.Repeat(0.97f - r * 0.09f, 1f), 0.7f, 1f);
                b.spawn = Time.time + r * 0.08f + c * 0.02f;

                var go = new GameObject("Brick");
                go.transform.SetParent(transform, false);
                go.transform.position = b.pos;
                go.transform.localScale = Vector3.zero;
                b.t = go.transform;
                b.sr = go.AddComponent<SpriteRenderer>();
                b.sr.sprite = brickSprite;
                b.sr.color = BrickDisplay(b);
                b.sr.sortingOrder = 0;
                b.glow = NewSprite("Glow", glowSprite, b.t, new Color(b.baseCol.r, b.baseCol.g, b.baseCol.b, 0.30f), -1);
                b.glow.transform.localScale = new Vector3(1.9f, 1.2f, 1);
                bricks.Add(b);
            }
        }
        bricksLeft = bricks.Count;
    }

    void ResetBall()
    {
        state = State.Ready;
        combo = 0;
        trail.emitting = false;
        trail.Clear();
        ballSpeed = baseBallSpeed + 0.5f * (level - 1);
        PlaceBallOnPaddle();
    }

    void PlaceBallOnPaddle()
    {
        ballPos = new Vector2(paddleX, PaddleY + PaddleH / 2f + BallR + 0.01f);
        ball.position = ballPos;
    }

    void Launch()
    {
        float ang = Random.Range(-25f, 25f) * Mathf.Deg2Rad;
        ballVel = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * ballSpeed;
        trail.Clear();
        trail.emitting = true;
        state = State.Playing;
    }

    // =====================================================================
    //  LOOP PRINCIPAL
    // =====================================================================
    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        stateTimer += dt;

        FitCamera();
        UpdateCamera(dt);
        UpdateStars();
        UpdatePaddle(dt);
        UpdateBricksVisual(dt);

        switch (state)
        {
            case State.Ready:
                PlaceBallOnPaddle();
                if (Pressed()) Launch();
                break;
            case State.Playing:
                StepBall(dt);
                break;
            case State.LevelClear:
                if (stateTimer > 1.8f)
                {
                    level++;
                    BuildLevel();
                    ResetBall();
                }
                break;
            case State.GameOver:
                if (stateTimer > 0.6f && Pressed()) StartGame();
                break;
        }
    }

    void SetState(State s) { state = s; stateTimer = 0; }

    void UpdateCamera(float dt)
    {
        Vector2 off = shake > 0.001f ? Random.insideUnitCircle * shake : Vector2.zero;
        cam.transform.position = new Vector3(off.x, off.y, -10);
        shake = Mathf.MoveTowards(shake, 0, dt * 1.6f);
    }

    void UpdateStars()
    {
        for (int i = 0; i < stars.Length; i++)
        {
            var p = stars[i].position;
            p.y -= starSpeed[i] * Time.deltaTime;
            if (p.y < -7f) p.y = 7f;
            stars[i].position = p;
            var sr = stars[i].GetComponent<SpriteRenderer>();
            float a = 0.25f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 1.5f + starPhase[i]));
            sr.color = new Color(0.8f, 0.9f, 1f, a);
        }
    }

    void UpdatePaddle(float dt)
    {
        // entrada
        Vector2 p;
        if (GetPointer(out p) && Mathf.Abs(p.x - lastPointerX) > 3f)
        {
            usePointer = true;
            lastPointerX = p.x;
        }
        float axis = KeyAxis();
        if (Mathf.Abs(axis) > 0.01f) usePointer = false;

        if (usePointer)
            targetX = cam.ScreenToWorldPoint(new Vector3(lastPointerX, 0, 10)).x;
        else
            targetX += axis * 12f * dt;

        float lim = HalfW - paddleWidth / 2f;
        targetX = Mathf.Clamp(targetX, -lim, lim);
        paddleX = Mathf.Lerp(paddleX, targetX, 1f - Mathf.Exp(-25f * dt));

        paddlePunch = Mathf.MoveTowards(paddlePunch, 0, dt * 6f);
        paddle.position = new Vector3(paddleX, PaddleY, 0);
        paddle.localScale = new Vector3(paddleWidth / 1.6f * (1f + 0.08f * paddlePunch),
                                        1f - 0.35f * paddlePunch, 1f);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3) + c1 * Mathf.Pow(x - 1f, 2);
    }

    void UpdateBricksVisual(float dt)
    {
        foreach (var b in bricks)
        {
            if (!b.alive) continue;
            float k = Mathf.Clamp01((Time.time - b.spawn) / 0.4f);
            b.flash = Mathf.MoveTowards(b.flash, 0, dt * 5f);
            float s = BW * EaseOutBack(k) * (1f + 0.18f * b.flash);
            b.t.localScale = new Vector3(s, s, 1);
            b.sr.color = Color.Lerp(BrickDisplay(b), Color.white, b.flash);
        }
    }

    // =====================================================================
    //  FÍSICA DA BOLA (manual, sem tunelamento)
    // =====================================================================
    void StepBall(float dt)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(ballSpeed * dt / 0.08f));
        float sdt = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            ballPos += ballVel * sdt;
            CollideWalls();
            CollidePaddle();
            CollideBricks();
            if (state != State.Playing) return;
            if (ballPos.y < KillY) { LoseLife(); return; }
        }
        // mantém velocidade constante e evita trajetória horizontal
        float minY = 0.28f * ballSpeed;
        if (Mathf.Abs(ballVel.y) < minY)
        {
            ballVel.y = minY * (ballVel.y >= 0 ? 1 : -1);
        }
        ballVel = ballVel.normalized * ballSpeed;
        ball.position = ballPos;
    }

    void CollideWalls()
    {
        if (ballPos.x < -HalfW + BallR) { ballPos.x = -HalfW + BallR; ballVel.x = Mathf.Abs(ballVel.x); WallFx(); }
        else if (ballPos.x > HalfW - BallR) { ballPos.x = HalfW - BallR; ballVel.x = -Mathf.Abs(ballVel.x); WallFx(); }
        if (ballPos.y > Top - BallR) { ballPos.y = Top - BallR; ballVel.y = -Mathf.Abs(ballVel.y); WallFx(); }
    }

    void WallFx() { Burst(ballPos, new Color(0.5f, 0.9f, 1f), 3, 2.5f); }

    void CollidePaddle()
    {
        if (ballVel.y >= 0) return;
        float hw = paddleWidth / 2f, hh = PaddleH / 2f;
        float cx = Mathf.Clamp(ballPos.x, paddleX - hw, paddleX + hw);
        float cy = Mathf.Clamp(ballPos.y, PaddleY - hh, PaddleY + hh);
        float dx = ballPos.x - cx, dy = ballPos.y - cy;
        if (dx * dx + dy * dy > BallR * BallR) return;

        float t = Mathf.Clamp((ballPos.x - paddleX) / hw, -1f, 1f);
        float ang = t * 62f * Mathf.Deg2Rad;
        ballVel = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * ballSpeed;
        ballPos.y = PaddleY + hh + BallR;
        combo = 0;
        paddlePunch = 1f;
        Burst(ballPos, new Color(0.5f, 0.95f, 1f), 6, 3f);
        AddShake(0.04f);
    }

    void CollideBricks()
    {
        foreach (var b in bricks)
        {
            if (!b.alive) continue;
            float hw = BW / 2f, hh = BH / 2f;
            if (Mathf.Abs(ballPos.x - b.pos.x) > hw + BallR || Mathf.Abs(ballPos.y - b.pos.y) > hh + BallR) continue;

            float cx = Mathf.Clamp(ballPos.x, b.pos.x - hw, b.pos.x + hw);
            float cy = Mathf.Clamp(ballPos.y, b.pos.y - hh, b.pos.y + hh);
            float dx = ballPos.x - cx, dy = ballPos.y - cy;
            if (dx * dx + dy * dy > BallR * BallR) continue;

            bool horizontal;
            if (dx * dx + dy * dy > 1e-6f) horizontal = Mathf.Abs(dx) > Mathf.Abs(dy);
            else horizontal = Mathf.Abs(ballPos.x - b.pos.x) / hw > Mathf.Abs(ballPos.y - b.pos.y) / hh;

            if (horizontal)
            {
                float sgn = ballPos.x >= b.pos.x ? 1f : -1f;
                ballVel.x = Mathf.Abs(ballVel.x) * sgn;
                ballPos.x = b.pos.x + sgn * (hw + BallR);
            }
            else
            {
                float sgn = ballPos.y >= b.pos.y ? 1f : -1f;
                ballVel.y = Mathf.Abs(ballVel.y) * sgn;
                ballPos.y = b.pos.y + sgn * (hh + BallR);
            }

            HitBrick(b);
            break; // um tijolo por sub-passo
        }
    }

    void HitBrick(Brick b)
    {
        b.hp--;
        b.flash = 1f;
        combo++;
        score += 10 + combo * 2;

        if (b.hp <= 0)
        {
            b.alive = false;
            bricksLeft--;
            bricksBroken++;
            Burst(b.pos, b.baseCol, 16, 5.5f);
            Burst(b.pos, Color.white, 4, 3f);
            AddShake(0.07f);
            Destroy(b.t.gameObject);
            ballSpeed = Mathf.Min(baseBallSpeed + 0.5f * (level - 1) + 4f, ballSpeed + 0.04f);

            if (bricksLeft <= 0)
            {
                SetState(State.LevelClear);
                trail.emitting = false;
                score += 100 * level;
                for (int i = 0; i < 5; i++)
                    Burst(new Vector2(Random.Range(-3f, 3f), Random.Range(0f, 4f)),
                          Color.HSVToRGB(Random.value, 0.6f, 1f), 30, 6f);
            }
        }
        else
        {
            Burst(b.pos, b.baseCol, 5, 3f);
        }
    }

    void LoseLife()
    {
        lives--;
        combo = 0;
        AddShake(0.35f);
        Burst(new Vector2(Mathf.Clamp(ballPos.x, -HalfW, HalfW), -5.8f), new Color(1f, 0.35f, 0.4f), 30, 6f);
        trail.emitting = false;
        if (lives <= 0)
        {
            SetState(State.GameOver);
            ball.position = new Vector3(0, -20, 0);
        }
        else ResetBall();
    }

    void AddShake(float v) { shake = Mathf.Max(shake, v); }

    // =====================================================================
    //  INPUT (funciona com Input System novo e antigo)
    // =====================================================================
    bool GetPointer(out Vector2 p)
    {
        p = default;
#if ENABLE_INPUT_SYSTEM
        var ptr = Pointer.current;
        if (ptr != null) { p = ptr.position.ReadValue(); return true; }
        return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
        p = Input.mousePosition;
        return true;
#else
        return false;
#endif
    }

    bool Pressed()
    {
        bool r = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame)) r = true;
        var m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame) r = true;
        var t = Touchscreen.current;
        if (t != null && t.primaryTouch.press.wasPressedThisFrame) r = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.UpArrow)
            || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)) r = true;
#endif
        return r;
    }

    float KeyAxis()
    {
        float a = 0;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            if (k.leftArrowKey.isPressed || k.aKey.isPressed) a -= 1;
            if (k.rightArrowKey.isPressed || k.dKey.isPressed) a += 1;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) a -= 1;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) a += 1;
#endif
        return Mathf.Clamp(a, -1f, 1f);
    }

    // =====================================================================
    //  HUD
    // =====================================================================
    void InitStyles()
    {
        int h = Screen.height;
        if (sBig != null && lastFont == h) return;
        lastFont = h;
        sBig = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(h * 0.07f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sMid = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(h * 0.035f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sSmall = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(h * 0.028f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
    }

    void Label(Rect r, string s, GUIStyle st, Color c)
    {
        var o = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.6f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
        GUI.color = c;
        GUI.Label(r, s, st);
        GUI.color = o;
    }

    void OnGUI()
    {
        if (cam == null) return;
        InitStyles();
        float W = Screen.width, H = Screen.height, bar = H * 0.075f;

        // barra superior
        GUI.color = new Color(0, 0, 0, 0.35f);
        GUI.DrawTexture(new Rect(0, 0, W, bar), Texture2D.whiteTexture);
        GUI.color = Color.white;

        Label(new Rect(W * 0.03f, 0, W * 0.35f, bar), "PONTOS  " + score.ToString("D6"), sSmall, new Color(0.7f, 0.95f, 1f));
        sSmall.alignment = TextAnchor.MiddleCenter;
        Label(new Rect(W * 0.35f, 0, W * 0.3f, bar), "NÍVEL " + level, sSmall, new Color(1f, 0.85f, 0.4f));
        sSmall.alignment = TextAnchor.MiddleLeft;

        // vidas (bolinhas)
        float d = bar * 0.38f;
        for (int i = 0; i < lives; i++)
        {
            GUI.color = new Color(1f, 0.4f, 0.55f);
            GUI.DrawTexture(new Rect(W - W * 0.03f - (i + 1) * (d * 1.5f), (bar - d) / 2f, d, d), circleTex);
        }
        GUI.color = Color.white;

        if (combo >= 3 && state == State.Playing)
            Label(new Rect(0, bar, W, bar), "COMBO x" + combo, sMid, new Color(1f, 0.8f, 0.3f, 0.9f));

        float blink = 0.55f + 0.45f * Mathf.Sin(Time.time * 4f);
        switch (state)
        {
            case State.Ready:
                Label(new Rect(0, H * 0.58f, W, H * 0.08f),
                      level == 1 && bricksBroken == 0 ? "CLIQUE OU ESPAÇO PARA LANÇAR" : "PREPARE-SE...  CLIQUE PARA LANÇAR",
                      sMid, new Color(1, 1, 1, blink));
                break;
            case State.LevelClear:
                Label(new Rect(0, H * 0.35f, W, H * 0.12f), "NÍVEL " + level + " COMPLETO!", sBig, new Color(1f, 0.9f, 0.4f));
                break;
            case State.GameOver:
                GUI.color = new Color(0, 0, 0, 0.55f);
                GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
                GUI.color = Color.white;
                Label(new Rect(0, H * 0.30f, W, H * 0.12f), "FIM DE JOGO", sBig, new Color(1f, 0.4f, 0.5f));
                Label(new Rect(0, H * 0.45f, W, H * 0.08f), "Pontos: " + score + "   |   Nível: " + level, sMid, Color.white);
                Label(new Rect(0, H * 0.58f, W, H * 0.08f), "CLIQUE PARA REINICIAR", sMid, new Color(1, 1, 1, blink));
                break;
        }
    }
}
