# Loa Schedule

여러 명의 로스트아크 캐릭터와 주간 레이드를 관리하고, 조건에 맞는 공격대와 진행 순서를 자동으로 구성하는 관리자용 Windows WPF 애플리케이션입니다.

## 주요 기능

- 참여자와 참여자별 캐릭터 관리
- Lost Ark Open API를 이용한 캐릭터 클래스·아이템 레벨 조회
- 캐릭터별로 참가할 주간 레이드 1~3개 선택
- 레이드별 입장 레벨, 인원, 파티 크기, 관문 및 우선순위 관리
- 참여자별 가능 시간 등록 및 공통 가능 시간 가중치 적용
- 직업·세팅별 시너지와 시너지 적용 불가 규칙 관리
- 4인·8인 레이드 공격대 자동 편성
- 한 공격대 내 동일 참여자의 캐릭터 중복 방지
- 4인 파티 내 동일 직업 중복 방지
- 서포터 우선 배치 및 서포터가 없어도 공석을 포함한 편성 허용
- 전투력 균형, 시너지, 가능 시간, 캐릭터 교체 최소화 점수 반영
- 가능한 경우 정원을 먼저 채우고 잔여 인원이 적은 공격대를 후순위 배치
- 참여자의 중간 이탈·재참여와 캐릭터 변경을 줄이는 진행 순서 추천
- 공격대 카드 UI, 수동 파티 교환, 미편성 캐릭터 교체 및 공석 투입
- 자동 편성 초안 전체 확정·확정 해제
- 매주 수요일 06:00 기준 주차 분리 및 관문 진행 관리
- 로컬 JSON 저장과 단일 EXE 배포

가능 시간은 강제 조건이 아닌 소프트 가중치입니다. 공통 시간이 없거나 시간을 등록하지 않은 참여자도 자동 편성에서 제외되지 않습니다.

## 기술 구성

- .NET 9
- WPF
- MVVM
- `System.Text.Json`
- Lost Ark Open API

## 프로젝트 구조

```text
src/
  LoaSchedule.App/             WPF 화면과 ViewModel
  LoaSchedule.Domain/          도메인 모델과 편성·추천 서비스
  LoaSchedule.Infrastructure/  JSON 저장소와 Lost Ark API 연동
tests/
  LoaSchedule.SmokeTests/      주요 기능 스모크 테스트
```

## 개발 환경에서 실행

Windows에서 .NET 9 SDK가 필요합니다.

```powershell
dotnet restore LoaSchedule.slnx
dotnet run --project src/LoaSchedule.App/LoaSchedule.App.csproj
```

## 테스트

```powershell
dotnet build LoaSchedule.slnx
dotnet run --project tests/LoaSchedule.SmokeTests/LoaSchedule.SmokeTests.csproj --no-build
```

스모크 테스트는 JSON 저장·불러오기, API 응답 파싱과 캐시, 주간 초기화, 레이드 추천, 자동 편성, 가능 시간 가중치, 동일 직업 분리, 공격대 진행 순서 및 확정 편성 보호를 검사합니다.

## 단일 EXE 배포

```powershell
.\publish-single-file.ps1
```

배포 결과는 다음 경로에 생성됩니다.

```text
artifacts/release/LoaSchedule.App.exe
```

실행 시 애플리케이션은 EXE와 같은 폴더의 `data.json`을 읽고 저장합니다. 새 사용자에게 공유할 때는 EXE만 전달하면 새 데이터 파일이 생성됩니다.

## 기본 사용 순서

1. `참여자 · 캐릭터` 탭에서 본인의 Lost Ark API 키를 저장합니다.
2. 참여자를 추가하고 캐릭터명을 API로 조회해 캐릭터를 등록합니다.
3. 필요한 경우 `가능 시간` 탭에서 참여자별 시간을 등록합니다.
4. `레이드 기준`과 `시너지 규칙`을 확인하거나 수정합니다.
5. `주간 편성`에서 캐릭터별 참가 레이드를 선택합니다.
6. `자동 편성 결과`에서 가중치를 조절하고 초안을 생성합니다.
7. 공격대 카드와 추천 진행 순서를 확인한 뒤 수동 조정하거나 전체 확정합니다.

## 데이터 및 API 키 주의사항

`data.json`에는 참여자, 캐릭터, 편성 결과와 Lost Ark API 키가 저장됩니다. 이 파일은 Git에서 제외되어 있으며 다른 사람에게 공유하거나 공개 저장소에 업로드하지 않아야 합니다.
