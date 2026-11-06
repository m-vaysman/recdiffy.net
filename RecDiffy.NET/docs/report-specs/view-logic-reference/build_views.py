# REFERENCE ONLY, not runnable on its own. Computes the per-view data (views.json) for the 13-view workbook:
# blotter status, age, impact, ranking and suggested action; side-by-side cell states; lifecycle chains; allocation checks;
# cash ladder and face decompositions; funnel, heatmap, ageing, tolerance usage and audit-trail rows.
# Its first lines load raw rows from an analysis prototype that stood in for the engine and is not included.
# In RecDiffy.Excel the same inputs come from ReconciliationResult (Entries, Rows, Canonical, Raw, Trace, Projection).
# Builds per-view sample data from rec_result.json (+ raw workbook rows) for the xlsx and html builders.
import io, contextlib, json, collections, datetime
from decimal import Decimal as D
V='/tmp/claude-0/-home-claude/cabf533a-d1e4-5388-9d72-03e17a3a3e9b/scratchpad/viz/'
src=open('/tmp/claude-0/-home-claude/cabf533a-d1e4-5388-9d72-03e17a3a3e9b/scratchpad/scripts/diffcheck_even.py').read().split("for k,v in sorted(res.items())")[0]
with contextlib.redirect_stdout(io.StringIO()): exec(src)
R=json.load(open(V+'rec_result.json')); E=R['entries']; ASOF=datetime.date(2025,6,13)
Lb={x['id']:x for x in Ls}; Rb={x['id']:x for x in Rs}
def dt(s): return datetime.date.fromisoformat(s)
def bdays(a,b):
    n=0; d=a
    while d<b:
        d+=datetime.timedelta(days=1)
        if d.weekday()<5: n+=1
    return n
def sgn(side): return -1 if side in('BUY','PAY_FIXED') else 1
def proj(e): return e['leftProjection'] or e['rightProjection']
def cp(e): return proj(e)['Counterparty']
CCY_ORDER=['USD','EUR','GBP','CHF','CAD','JPY']
ACTION={'Price':'Confirm execution price with counterparty','Quantity':'Confirm fill quantity (partial fill?)','SettleDate':'Align settlement date / SSI convention',
        'Fees':'Check commission schedule','LeftOnly':'Chase confirmation: not on street file','RightOnly':'DK: investigate alleged trade'}
views={}
# ---------- BO1 blotter
bl=[]
for e in E:
    if e['outcome']=='Break':
        p=e['leftProjection']; root=e['rootFields'][0]; d=next(x for x in e['diffs'] if x['field']==root)
        net=next((x for x in e['diffs'] if x['field']=='NetCash'),None)
        impact=abs(D(p['NetCash'])) if root=='SettleDate' else abs(D(net['delta']))
        bl.append(dict(entry=e['entryId'],grp=e['oracleGroup'],type='Field break',root=root,ours=d['left'],theirs=d['right'],delta=d['delta'],p=p,impact=impact,action=ACTION[root],label=e['instrumentLabel']))
    elif e['alignment'] in('LeftOnly','RightOnly'):
        p=proj(e); diag=e.get('diagnostics',[])
        bl.append(dict(entry=e['entryId'],grp=e['oracleGroup'],type='Unmatched ours' if e['alignment']=='LeftOnly' else 'Unmatched theirs',root='(whole trade)',ours=p['NetCash'] if e['alignment']=='LeftOnly' else '',theirs=p['NetCash'] if e['alignment']=='RightOnly' else '',delta='',p=p,impact=abs(D(p['NetCash'])),
                       action=('Set up security / map identifier, then DK review' if 'UnresolvedInstrument' in diag else ACTION[e['alignment']]),label=e['instrumentLabel']))
for b in bl:
    sd=dt(b['p']['SettleDate']); b['age']=bdays(dt(b['p']['TradeDate']),ASOF)
    b['status']='Past settle date' if sd<ASOF else 'Settles today' if sd==ASOF else 'Upcoming'
SR={'Past settle date':0,'Settles today':1,'Upcoming':2}
bl.sort(key=lambda b:(SR[b['status']],CCY_ORDER.index(b['p']['Currency']),-b['impact'],b['entry']))
views['BO1']=[dict(rank=i+1,entry=b['entry'],oracleGroup=b['grp'],status=b['status'],type=b['type'],counterparty=b['p']['Counterparty'],instrument=b['label'],asset=b['p']['AssetClass'],side=b['p']['Side'],
                   tradeDate=b['p']['TradeDate'],settleDate=b['p']['SettleDate'],ageBD=b['age'],field=b['root'],ours=b['ours'],theirs=b['theirs'],delta=b['delta'],ccy=b['p']['Currency'],impact=str(b['impact']),action=b['action']) for i,b in enumerate(bl)]
# ---------- BO2 side-by-side
FIELDS=['Quantity','Price','SettleDate','Accrued','Fees','NetCash']
sbs=[]
for e in E:
    if e['alignment']=='Matched' and e['outcome']!='Exact':
        lp,rp=e['leftProjection'],e['rightProjection']; dmap={d['field']:d for d in e['diffs']}
        row=dict(entry=e['entryId'],oracleGroup=e['oracleGroup'],counterparty=lp['Counterparty'],instrument=e['instrumentLabel'],asset=lp['AssetClass'],side=lp['Side'],ccy=lp['Currency'],outcome=e['outcome'],pass_=e['committedBy'],cells={})
        for f in FIELDS:
            d=dmap.get(f)
            state='same' if d is None else ('attributed' if d['attributedTo'] else ('break' if d['outcome']=='Break' else 'tolerance'))
            row['cells'][f]=dict(ours=lp[f],theirs=(d['right'] if d else rp[f]),state=state,delta=(d['delta'] if d else None))
        sbs.append(row)
sbs.sort(key=lambda r:(r['outcome']!='Break',r['oracleGroup']))
views['BO2']=sbs
# ---------- BO3 stacked raw vs canonical
picks=[('G0011','32nds price + BAML date text'),('G0139','Price break'),('G0144','Partial fill (quantity break)'),('G0149','Settle-date break'),('G0157','Fee break (fee inferred from net)'),('G0127','FX rate within tolerance')]
def find(grp): return next(e for e in E if e['oracleGroup']==grp)
# dealer view + dirty price picks by row id
extra=[('BC-51721586','Dealer view: side inverted, BARC date dd/MM/yyyy'),('CG-51174451','Dirty price converted to clean')]
for rid,why in extra:
    e=next(e for e in E if rid in e['right']); picks.append((e['oracleGroup'],why))
IRAW=['Internal_Trade_ID','Counterparty_Code','Ticker','Trade_Date','Settle_Date','Side','Quantity_Notional','Clean_Price_Rate','Accrued_Interest','Commission_Fees','Net_Settlement_Amount']
CRAW=['Cpty_Report_ID','Reporting_Counterparty_Name','Counterparty_LEI','Primary_Security_ID','Security_ID_Source','Trade_Date','Value_Settle_Date','Dir_Ind','Reported_Qty_Notional','Exec_Price_Rate','Reported_Accrued_Int','Price_Quote_Convention','Net_Cash_Amount','Reporting_Perspective']
def rawfmt(v): return v.strftime('%Y-%m-%d') if isinstance(v,datetime.datetime) else ('' if v is None else str(v))
st=[]
for grp,why in picks:
    e=find(grp); lid=e['left'][0]; rid=e['right'][0]
    st.append(dict(oracleGroup=grp,entry=e['entryId'],why=why,outcome=e['outcome'],pass_=e['committedBy'],
                   internalRaw={k:rawfmt(Lb[lid]['raw'][k]) for k in IRAW},streetRaw={k:rawfmt(Rb[rid]['raw'][k]) for k in CRAW},
                   ours=e['leftProjection'],theirs=e['rightProjection'],diffs=e['diffs'],trace=traces.get(rid,[])))
views['BO3']=st
# ---------- BO4 lifecycle
ch=[]
for e in E:
    if e['suppressedRight']:
        rows=[]
        for rid,dis in [(e['suppressedRight'][0],'Superseded'),(e['suppressedRight'][1],'CancelEvent'),(e['right'][0],'Survivor (matched)')]:
            r=Rb[rid]; raw=r['raw']
            rows.append(dict(report=rid,action=raw['Trans_Lifecycle_Action'],target=raw['Cancels_Prior_Report_ID'] or '',qty=str(r['qty']),price=str(r['px'].normalize()),settle=str(r['sd']),net=str(r['net']),disposition=dis))
        g=next(g for g in o['groups'] if g['group_id']==e['oracleGroup'])
        ch.append(dict(oracleGroup=e['oracleGroup'],entry=e['entryId'],counterparty=e['leftProjection']['Counterparty'],instrument=e['instrumentLabel'],bust=g['details'].get('bust_type'),
                       matchedInternal=e['left'][0],internalPrice=e['leftProjection']['Price'],internalQty=e['leftProjection']['Quantity'],internalNet=e['leftProjection']['NetCash'],pass_=e['committedBy'],outcome=e['outcome'],rows=rows))
views['BO4']=ch
# ---------- BO5 allocations
al=[]
for e in E:
    if e['composition']=='ManyToOne':
        r=Rb[e['right'][0]]
        mem=[dict(id=i,book=Lb[i]['book'],qty=str(Lb[i]['qty']),price=str(Lb[i]['px'].normalize()),net=str(Lb[i]['net'])) for i in e['left']]
        al.append(dict(oracleGroup=e['oracleGroup'],entry=e['entryId'],counterparty=e['leftProjection']['Counterparty'],instrument=e['instrumentLabel'],side=e['leftProjection']['Side'],
                       block=dict(id=r['id'],qty=str(r['qty']),price=str(r['px'].normalize()),net=str(r['net'])),members=mem,sumQty=e['leftProjection']['Quantity'],sumNet=e['leftProjection']['NetCash'],outcome=e['outcome']))
views['BO5']=al
# ---------- BO6 cash ladder (signed: receive +, pay -)
lad=collections.defaultdict(lambda: dict(ours=D(0),theirs=D(0),unmatchedOurs=D(0),unmatchedTheirs=D(0),inGroup=D(0),items=0))
for e in E:
    if e['alignment']=='Matched':
        lp,rp=e['leftProjection'],e['rightProjection']
        lo=sgn(lp['Side'])*D(lp['NetCash']); ro=sgn(rp['Side'])*D(rp['NetCash'])
        k1=(lp['SettleDate'],lp['Currency']); k2=(rp['SettleDate'],rp['Currency'])
        lad[k1]['ours']+=lo; lad[k1]['items']+=1; lad[k2]['theirs']+=ro
        if k1==k2: lad[k1]['inGroup']+=ro-lo
        else: lad[k1]['inGroup']-=lo; lad[k2]['inGroup']+=ro   # settle-date breaks move cash between dates
    elif e['alignment']=='LeftOnly':
        p=e['leftProjection']; k=(p['SettleDate'],p['Currency']); v=sgn(p['Side'])*D(p['NetCash']); lad[k]['ours']+=v; lad[k]['unmatchedOurs']-=v; lad[k]['items']+=1
    else:
        p=e['rightProjection']; k=(p['SettleDate'],p['Currency']); v=sgn(p['Side'])*D(p['NetCash']); lad[k]['theirs']+=v; lad[k]['unmatchedTheirs']+=v; lad[k]['items']+=1
ladder=[]
for (sd,ccy),v in sorted(lad.items(),key=lambda kv:(kv[0][0],CCY_ORDER.index(kv[0][1]))):
    diff=v['theirs']-v['ours']; assert diff==v['unmatchedOurs']+v['unmatchedTheirs']+v['inGroup'], (sd,ccy)
    ladder.append(dict(settleDate=sd,ccy=ccy,items=v['items'],ours=str(v['ours']),theirs=str(v['theirs']),diff=str(diff),unmatchedOurs=str(v['unmatchedOurs']),unmatchedTheirs=str(v['unmatchedTheirs']),inGroup=str(v['inGroup'])))
views['BO6']=ladder
# ---------- MO1 control totals: counts by counterparty x asset; USD bond face decomposition
cnt=collections.defaultdict(lambda:[0,0])
for x in Ls: cnt[(x['cpty'],x['asset'])][0]+=1
for x in live_R: cnt[(x['cpty'],x['asset'])][1]+=1
cps=sorted({k[0] for k in cnt}); assets=['Bond','Equity','Forward','Swap']
views['MO1']=dict(counterparties=cps,assets=assets,counts={f"{c}|{a}":cnt[(c,a)] for c in cps for a in assets})
face=[]
for c in cps:
    sel=lambda p: p and p['AssetClass']=='Bond' and p['Currency']=='USD' and p['Counterparty']==c
    ours=sum(D(x['qty']) for x in Ls if x['cpty']==c and x['asset']=='Bond' and x['ccy']=='USD')
    theirs=sum(D(x['qty']) for x in live_R if x['cpty']==c and x['asset']=='Bond' and x['ccy']=='USD')
    lo=-sum(D(e['leftProjection']['Quantity']) for e in E if e['alignment']=='LeftOnly' and sel(e['leftProjection']))
    ro=sum(D(e['rightProjection']['Quantity']) for e in E if e['alignment']=='RightOnly' and sel(e['rightProjection']))
    mg=sum(D(e['rightProjection']['Quantity'])-D(e['leftProjection']['Quantity']) for e in E if e['alignment']=='Matched' and sel(e['leftProjection']))
    no=sum(1 for x in Ls if x['cpty']==c and x['asset']=='Bond' and x['ccy']=='USD'); nt=sum(1 for x in live_R if x['cpty']==c and x['asset']=='Bond' and x['ccy']=='USD')
    assert theirs-ours==lo+ro+mg, c
    face.append(dict(counterparty=c,rowsOurs=no,rowsTheirs=nt,ours=str(ours),theirs=str(theirs),delta=str(theirs-ours),leftOnly=str(lo),rightOnly=str(ro),matchedDelta=str(mg)))
views['MO1face']=face
# ---------- MO2 scorecard
sc=[]
for c in cps:
    es=[e for e in E if cp(e)==c]
    m=[e for e in es if e['alignment']=='Matched']
    itemsL=sum(len(e['left']) if e['composition'] in('ManyToOne',) else (1 if e['left'] else 0) for e in es)
    sc.append(dict(counterparty=c,matched=len(m),exact=sum(e['outcome']=='Exact' for e in m),tol=sum(e['outcome']=='WithinTolerance' for e in m),brk=sum(e['outcome']=='Break' for e in m),
                   leftOnly=sum(e['alignment']=='LeftOnly' for e in es),rightOnly=sum(e['alignment']=='RightOnly' for e in es),
                   rowsOurs=sum(1 for x in Ls if x['cpty']==c),rowsTheirs=sum(1 for x in Rs if x['cpty']==c)))
for s in sc:
    s['matchedRowsOurs']=sum(len(e['left']) for e in E if cp(e)==s['counterparty'] and e['alignment']=='Matched')
    s['matchRate']=round(s['matchedRowsOurs']/s['rowsOurs'],4); s['cleanRate']=round((s['exact']+s['tol'])/max(1,s['matched']),4)
tot=dict(rows=500,rowsOurs=300,rowsTheirs=200,entries=len(E),matched=sum(e['alignment']=='Matched' for e in E),exact=sum(e['outcome']=='Exact' for e in E),tol=sum(e['outcome']=='WithinTolerance' for e in E),
         brk=sum(e['outcome']=='Break' for e in E),leftOnly=sum(e['alignment']=='LeftOnly' for e in E),rightOnly=sum(e['alignment']=='RightOnly' for e in E),
         matchedRowsOurs=sum(len(e['left']) for e in E if e['alignment']=='Matched'),suppressed=10,
         byPass={p:sum(e['committedBy']==p for e in E) for p in ['P1','P2','P3','P4']})
views['MO2']=dict(total=tot,byCounterparty=sc)
# ---------- MO3 funnel
views['MO3']=[dict(stage='Rows read',left=300,right=200,note='Internal_Trades and Counterparty_Trades tabs'),
              dict(stage='After lifecycle collapse',left=300,right=190,note='5 NEW/CANC/CORR chains: 10 rows suppressed'),
              dict(stage='Items after packages',left=292,right=190,note='8 two-leg packages: 16 rows become 8 items'),
              dict(stage='P1 shared UTI',left=-86,right=-86,note='commits 86 groups'),
              dict(stage='P2 natural key incl. quantity',left=-81,right=-81,note='commits 81 groups'),
              dict(stage='P3 group sum (N:1)',left=-36,right=-12,note='commits 12 allocation groups: 36 internal rows against 12 street blocks'),
              dict(stage='P4 reduced key (quantity breaks)',left=-4,right=-4,note='commits 4 partial fills'),
              dict(stage='Residue',left=85,right=7,note='85 unmatched ours, 7 unmatched theirs')]
# ---------- MO4 heatmap
cats=['Price','Quantity','SettleDate','Fees','Unmatched ours','Unmatched theirs']
hm=collections.Counter()
for b in views['BO1']:
    c=b['field'] if b['type']=='Field break' else b['type']; hm[(b['counterparty'],c)]+=1
views['MO4']=dict(categories=cats,counterparties=cps,counts={f"{c}|{k}":hm[(c,k)] for c in cps for k in cats})
# ---------- MO5 aging
ag=collections.Counter(); stt=collections.Counter()
for b in views['BO1']:
    c=b['field'] if b['type']=='Field break' else b['type']; ag[(c,b['ageBD'])]+=1; stt[(c,b['status'])]+=1
views['MO5']=dict(categories=cats,ages=[1,2,3],statuses=['Past settle date','Settles today','Upcoming'],age={f"{c}|{a}":ag[(c,a)] for c in cats for a in [1,2,3]},status={f"{c}|{s}":stt[(c,s)] for c in cats for s in ['Past settle date','Settles today','Upcoming']})
# ---------- MO6 tolerance usage
TOL={'Accrued':D('1.25'),'Price':D('0.0001'),'Fees':D('0.05')}
tu=[]
for e in E:
    if e['outcome']=='WithinTolerance':
        for d in e['diffs']:
            if d['outcome']=='WithinTolerance' and not d['attributedTo']:
                used=abs(D(d['delta']))/TOL[d['field']]
                tu.append(dict(entry=e['entryId'],oracleGroup=e['oracleGroup'],counterparty=e['leftProjection']['Counterparty'],instrument=e['instrumentLabel'],field=d['field'],ours=d['left'],theirs=d['right'],delta=d['delta'],tolerance=str(TOL[d['field']]),used=float(round(used,4))))
tu.sort(key=lambda t:-t['used'])
views['MO6']=tu
# ---------- MO7 audit trail (curated, matches the spec's worked samples)
views['MO7']=[
 dict(oracleGroup='G0011',entry=find('G0011')['entryId'],rows='OMS4152729 / BA-18777889',result='Matched, Exact',trail=[('P1','NoKey','BAX8101307164 is a broker reference, not a UTI'),('P2','Committed','(BAML, US91282CT687, Bond, 2025-06-10, SELL, 3000000.00)')]),
 dict(oracleGroup='G0159',entry=find('G0159')['entryId'],rows='OMS4044414 + OMS7849940 + OMS4003839 / JP-48051575',result='Matched N:1, Exact',trail=[('P1','Contest','3 internal rows and 1 street row share UTI …7CB88FD742: deferred'),('P2','NoCandidate','street 9,000 vs 2,200 / 5,400 / 1,400'),('P3','Committed','only subset conserving 9,000 and 1,026,045.00; search complete')]),
 dict(oracleGroup='G0144',entry=find('G0144')['entryId'],rows='OMS3512650 / CG-43060951',result='Matched, Break (Quantity)',trail=[('P1','NoKey','CGX1302998639 is a broker reference'),('P2','NoCandidate','1,000,000 vs 800,000'),('P3','NoCandidate','block has one internal row'),('P4','Committed','(CITI, UST77KS39V09, 2025-06-12, SELL)')]),
 dict(oracleGroup='G0171',entry=find('G0171')['entryId'],rows='OMS9369884 + OMS9929303 / JP-32003590',result='Matched package, Exact',trail=[('Stage 2','Package','legs PLK-158918 netted: 278,961.22 − 259,124.12 = 19,837.10'),('P1','Committed','shared UTI …5C616F4921')]),
 dict(oracleGroup='G0179',entry=find('G0179')['entryId'],rows='OMS9063540 / CS-36468090 (CORR)',result='Matched after lifecycle collapse, Exact',trail=[('Stage 2','Lifecycle','CS-39633954 NEW superseded; CS-74239776 CANC suppressed; CORR survives'),('P1','Committed','shared UTI')]),
 dict(oracleGroup=find('G0184')['oracleGroup'],entry=find('G0184')['entryId'],rows='OMS7586315 / —',result='LeftOnly',trail=[('P1','NoKey','UTI_Exec_ID blank'),('P2','NoCandidate','no street row on (CITI, US4T8CATZT44, Bond, 2025-06-10, SELL, 1000000.00)'),('P3','NoCandidate','no street target in block'),('P4','NoCandidate','no street row on (CITI, US4T8CATZT44, 2025-06-10, SELL)'),('Hint','NearestCandidate','MS-58715991 shares the instrument but its counterparty is MSCO')]),
]
json.dump(views,open(V+'views.json','w'),indent=1,default=str)
print({k:(len(v) if isinstance(v,list) else 'dict') for k,v in views.items()})
print('blotter status', collections.Counter(b['status'] for b in views['BO1']), 'types', collections.Counter(b['type'] for b in views['BO1']))
print('scorecard total', tot)
print('tolerance top', views['MO6'][:3])
