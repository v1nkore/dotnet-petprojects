# K8sLab — Kubernetes, Helm и CI/CD

Pet-проект №9 из плана. Закрывает раздел 12 из «200 ответов»: **Контейнеры, Kubernetes, CI/CD**.
Деплоим AspNetLab (проект без внешних зависимостей — идеален для первого кластера).

## Что было сделано и проверено вживую

```bash
kind create cluster --name petlab                 # локальный кластер в Docker
docker build -t aspnetlab:0.1 ../AspNetLab        # multi-stage Dockerfile
kind load docker-image aspnetlab:0.1 --name petlab
kubectl apply -f k8s/deployment.yaml              # Deployment+ConfigMap+Service+HPA
kubectl rollout status deployment/aspnetlab       # 2/2 Running, probes зелёные
kubectl port-forward svc/aspnetlab 8088:80        # health: Healthy, POST -> 202
```

**Демо отката (проверено):** `kubectl set image ... api=aspnetlab:0.2` (несуществующий) →
новый под завис в `ErrImageNeverPull`, но **старые реплики продолжали обслуживать
трафик** — rolling update не убивает работающее, пока новое не пройдёт readiness.
`kubectl rollout undo` — и всё вернулось за секунды.

**Helm:** те же манифесты шаблонизированы в `chart/`, `helm upgrade --install aspnetlab ./chart` —
релиз deployed, ревизии откатываются `helm rollback`.

## Что где смотреть

| Тема | Где | Суть |
|---|---|---|
| Multi-stage образ | `../AspNetLab/Dockerfile` | SDK собирает → aspnet исполняет: ~220 МБ, без компилятора, non-root user |
| `.dockerignore` | `../AspNetLab/.dockerignore` | Пойманный баг: хостовые `obj/` с виндовыми путями ломали publish в контейнере |
| Deployment / probes / resources | `k8s/deployment.yaml` | Комментарии: liveness vs readiness, requests vs limits, OOMKilled |
| ConfigMap → .NET конфиг | там же | `Dispatcher__SendDelayMilliseconds`: `__` = вложенность секций |
| HPA | там же | манифест как документация (нужен metrics-server) |
| Helm-чарт | `chart/` | values, шаблоны, фишка `checksum/config` — смена ConfigMap перекатывает поды |
| CI/CD | `github-actions-ci.yml` | build → test → образ с тегом = SHA → helm upgrade `--wait` |

## Ключевые «почему» для собеса

- **liveness vs readiness:** провал liveness = рестарт («процесс завис»); провал readiness =
  под убирают из балансировки, но не трогают («прогреваюсь / БД недоступна»). Перепутать —
  получить каскадные рестарты при моргании зависимости.
- **requests vs limits:** requests — для планировщика, limits — потолок. CPU за лимитом —
  троттлинг, память за лимитом — **OOMKilled** (диагностика: `kubectl describe pod` →
  Last State: OOMKilled, exit code 137 → поднять лимит или чинить утечку).
- **Стратегии деплоя:** rolling (по умолчанию, было в демо) · blue-green (два стека,
  переключение трафика) · canary (процент трафика на новую версию, нужен Ingress/mesh).
- **Почему тег образа = SHA коммита:** `latest` не откатишь и не поймёшь, что задеплоено.

## Пересоздать окружение

```bash
kind create cluster --name petlab && docker build -t aspnetlab:0.1 ../AspNetLab && \
kind load docker-image aspnetlab:0.1 --name petlab && helm upgrade --install aspnetlab ./chart
# снести: kind delete cluster --name petlab
```

Подробности — в **study-guide.html**.
