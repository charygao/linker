<template>
    <div class="home-list-wrap absolute" >
        <el-table :data="state.page.List" stripe border style="width: 100%" :height="`${state.height}px`" size="small">
            <Device  @edit="handleDeviceEdit" @refresh="handlePageRefresh"></Device>
            <Tunnel  @edit="handleTunnelEdit" @refresh="handleTunnelRefresh" @connections="handleTunnelConnections"></Tunnel>
            <Tuntap  @edit="handleTuntapEdit" @refresh="handleTuntapRefresh"></Tuntap>
            <Forward @edit="handleForwardEdit" @sedit="handleSForwardEdit" @refresh="handleForwardRefresh"></Forward>  
            <!-- <Info></Info>           -->
            <el-table-column label="操作" width="54" fixed="right">
                <template #default="scope">
                    <el-popconfirm v-if="scope.row.showDel" confirm-button-text="确认"
                        cancel-button-text="取消" title="删除不可逆，是否确认?" @confirm="handleDel(scope.row.MachineId)">
                        <template #reference>
                            <el-button type="danger" size="small"><el-icon><Delete /></el-icon></el-button>
                        </template>
                    </el-popconfirm>
                </template>
            </el-table-column>
        </el-table>
        <div class="page t-c">
            <div class="page-wrap">
                <el-pagination small background layout="total,prev, pager, next" :total="state.page.Count"
                    :page-size="state.page.Request.Size" :current-page="state.page.Request.Page"
                    @current-change="handlePageChange" />
            </div>
        </div>
        <DeviceEdit v-if="state.showDeviceEdit" v-model="state.showDeviceEdit"  @change="handlePageChange" :data="state.deviceInfo"></DeviceEdit>
        <TunnelEdit v-if="tunnel.showEdit" v-model="tunnel.showEdit"  @change="handleTunnelChange"></TunnelEdit>
        <ConnectionsEdit v-if="connections.showEdit" v-model="connections.showEdit" ></ConnectionsEdit>
        <TuntapEdit v-if="tuntap.showEdit" v-model="tuntap.showEdit"  @change="handleTuntapChange"></TuntapEdit>
        <ForwardEdit v-if="forward.showEdit" v-model="forward.showEdit"  @change="handleForwardChange"></ForwardEdit>
        <SForwardEdit v-if="sforward.showEdit" v-model="sforward.showEdit"  @change="handleSForwardChange"></SForwardEdit>
    </div>
</template>
<script>
import { getSignInList, signInDel } from '@/apis/signin.js'
import { subWebsocketState } from '@/apis/request.js'
import { getTuntapInfo,refreshTuntap,getTuntapConnections,removeTuntapConnection } from '@/apis/tuntap'
import { getForwardInfo ,testTargetForwardInfo,testListenForwardInfo,getForwardConnections,removeForwardConnection} from '@/apis/forward'
import { getSForwardInfo,testLocalSForwardInfo} from '@/apis/sforward'
import { getTunnelInfo ,refreshTunnel} from '@/apis/tunnel'
import { injectGlobalData } from '@/provide.js'
import { reactive, onMounted, ref, nextTick, onUnmounted, computed, provide } from 'vue'
import Device from './Device.vue'
import DeviceEdit from './DeviceEdit.vue'
import Info from './Info.vue'
import Tuntap from './Tuntap.vue'
import TuntapEdit from './TuntapEdit.vue'
import Tunnel from './Tunnel.vue'
import TunnelEdit from './TunnelEdit.vue'
import Forward from './Forward.vue'
import ForwardEdit from './ForwardEdit.vue'
import SForwardEdit from './SForwardEdit.vue'
import ConnectionsEdit from './ConnectionsEdit.vue'
import { ElMessage } from 'element-plus'
export default {
    components: {Device,DeviceEdit,Info,Tunnel,TunnelEdit,ConnectionsEdit, Tuntap,TuntapEdit,  Forward,ForwardEdit,SForwardEdit },
    setup(props) {

        const globalData = injectGlobalData();
        const machineId = computed(() => globalData.value.config.Client.Id);
       
        const state = reactive({
            timer:0,
            page: {
                Request: { Page: 1, Size: 10, GroupId: globalData.value.groupid },
                Count: 0,
                List: []
            },

            showDeviceEdit:false,
            deviceInfo: null,

            height: computed(()=>globalData.value.height-60),
        });

        const tuntap = ref({
            timer:0,
            showEdit:false,
            current: null,
            list: {},
            hashcode: 0,
        });
        provide('tuntap',tuntap);
        const _getTuntapInfo = () => {
            if (globalData.value.connected) {
                getTuntapInfo(tuntap.value.hashcode.toString()).then((res) => {
                    tuntap.value.hashcode = res.HashCode;
                    if (res.List) {
                        for (let j in res.List) {
                            res.List[j].running = res.List[j].Status == 2;
                            res.List[j].loading = res.List[j].Status == 1;
                        }
                        tuntap.value.list = res.List;
                    }
                    tuntap.value.timer = setTimeout(_getTuntapInfo, 200);
                }).catch(() => {
                    tuntap.value.timer = setTimeout(_getTuntapInfo, 200);
                });
            } else {
                tuntap.value.timer = setTimeout(_getTuntapInfo, 1000);
            }
        }
        const handleTuntapEdit = (_tuntap)=>{
            tuntap.value.current = _tuntap;
            tuntap.value.showEdit = true;

        }
        const handleTuntapChange = (page) => {
            if(page){
                state.page.Request.Page = page;
            }
            _getSignList();
        }
        const handleTuntapRefresh = ()=>{
            refreshTuntap();
            ElMessage.success('刷新成功');
        }


        const tunnel = ref({
            timer:0,
            showEdit:false,
            current: null,
            list: {},
            hashcode: 0,
        });
        provide('tunnel',tunnel);
        const _getTunnelInfo = () => {
            if (globalData.value.connected) {
                getTunnelInfo(tunnel.value.hashcode.toString()).then((res) => {
                    tunnel.value.hashcode = res.HashCode;
                    if (res.List) {
                        tunnel.value.list = res.List;
                    }
                    tunnel.value.timer = setTimeout(_getTunnelInfo, 1000);
                }).catch(() => {
                    tunnel.value.timer = setTimeout(_getTunnelInfo, 1000);
                });
            } else {
                tunnel.value.timer = setTimeout(_getTunnelInfo, 1000);
            }
        }
        const handleTunnelEdit = (_tunnel)=>{
            tunnel.value.current = _tunnel;
            tunnel.value.showEdit = true;
        }
        const handleTunnelChange = () => {
            _getSignList();
        }
        const handleTunnelRefresh = ()=>{
            refreshTunnel();
            ElMessage.success('刷新成功');
        }

        const connections = ref({
            showEdit:false,
            speedCache: {},
            current:''
        });
        provide('connections',connections);

        const forwardConnections = ref({
            timer:0,
            list: {},
        });
        provide('forward-connections',forwardConnections);
        const _getForwardConnections = ()=>{
            if (globalData.value.connected) {
                getForwardConnections().then((res)=>{
                    parseConnections(res,removeForwardConnection);
                    forwardConnections.value.list = res;
                    forwardConnections.value.timer = setTimeout(_getForwardConnections, 1000);
                }).catch((e)=>{
                    forwardConnections.value.timer = setTimeout(_getForwardConnections, 1000);
                })
            }else {
                forwardConnections.value.timer = setTimeout(_getForwardConnections, 1000);
            }
        }
        const tuntapConnections = ref({
            timer:0,
            list: {},
        });
        provide('tuntap-connections',tuntapConnections);
        const _getTuntapConnections = ()=>{
            if (globalData.value.connected) {
                getTuntapConnections().then((res)=>{
                    parseConnections(res,removeTuntapConnection);
                    tuntapConnections.value.list = res;
                    tuntapConnections.value.timer = setTimeout(_getTuntapConnections, 1000);
                }).catch((e)=>{
                    tuntapConnections.value.timer = setTimeout(_getTuntapConnections, 1000);
                })
            }else {
                tuntapConnections.value.timer = setTimeout(_getTuntapConnections, 1000);
            }
        }
        const parseConnections = (_connections,removeFunc)=>{
            const caches = connections.value.speedCache;
            for(let machineId in _connections){
                const connection = _connections[machineId]; 
                connection.removeFunc = removeFunc;
                
                const key = `${connection.RemoteMachineId}-${connection.TransactionId}`;
                const cache = caches[key] || {SendBytes:0,ReceiveBytes:0};
                
                connection.SendBytesText = parseSpeed(connection.SendBytes - cache.SendBytes);
                connection.ReceiveBytesText = parseSpeed(connection.ReceiveBytes - cache.ReceiveBytes);

                cache.SendBytes = connection.SendBytes;
                cache.ReceiveBytes = connection.ReceiveBytes;
                caches[key] = cache;
            }
        }
        const parseSpeed = (num)=>{
            let index = 0;
            while(num >= 1024){
                num/=1024;
                index++;
            }
            return `${num.toFixed(2)}${['B/s','KB/s','MB/s','GB/s','TB/s'][index]}`;
        }
        const handleTunnelConnections = (machineId)=>{
            connections.value.current = machineId;
            connections.value.showEdit = true;
        }

        const forward = ref({
            timer:0,
            showEdit:false,
            current: null,
            list: {}
        });
        provide('forward',forward);
        const _getForwardInfo = () => {
            if (globalData.value.connected) {
                getForwardInfo().then((res) => {
                    forward.value.list = res;

                    forward.value.timer = setTimeout(_getForwardInfo, 1000);
                }).catch(() => {
                    forward.value.timer = setTimeout(_getForwardInfo, 1000);
                });
            }else {
                forward.value.timer = setTimeout(_getForwardInfo, 1000);
            }
        }
        const handleForwardEdit = (machineId)=>{
            forward.value.current = machineId;
            forward.value.machineName =state.page.List.filter(c=>c.MachineId == machineId)[0].MachineName ;
            forward.value.showEdit = true;
        }
        const handleForwardChange = () => {
            _getSignList();
        }
        const handleForwardRefresh = ()=>{
            _getSignList();
            ElMessage.success('刷新成功');
        }


        const sforward = ref({
            timer:0,
            showEdit:false,
            list: []
        });
        provide('sforward',sforward);
        const _getSForwardInfo = () => {
            if (globalData.value.connected) {
                getSForwardInfo().then((res) => {
                    sforward.value.list = res;
                    sforward.value.timer = setTimeout(_getSForwardInfo, 1000);
                }).catch(() => {
                    sforward.value.timer = setTimeout(_getSForwardInfo, 1000);
                });
            }else {
                sforward.value.timer = setTimeout(_getSForwardInfo, 1000);
            }
        }
        const handleSForwardEdit = ()=>{
            sforward.value.showEdit = true;
        }
        const handleSForwardChange = () => {
            _getSignList();
        }



        const _getSignList = () => {
            state.page.Request.GroupId = globalData.value.groupid;
            getSignInList(state.page.Request).then((res) => {
                state.page.Request = res.Request;
                state.page.Count = res.Count;
                for (let j in res.List) {
                    res.List[j].showTunnel = machineId.value != res.List[j].MachineId;
                    res.List[j].showForward = machineId.value != res.List[j].MachineId;
                    res.List[j].showSForward = machineId.value == res.List[j].MachineId;
                    res.List[j].showDel = machineId.value != res.List[j].MachineId && res.List[j].Connected == false;
                    res.List[j].isSelf = machineId.value == res.List[j].MachineId;
                }
                state.page.List = res.List.sort((a,b)=>b.Connected - a.Connected);
            }).catch((err) => { });
        }
        const _getSignList1 = () => {
            if (globalData.value.connected) {
                state.page.Request.GroupId = globalData.value.groupid;
                getSignInList(state.page.Request).then((res) => {
                    for (let j in res.List) {
                        const item = state.page.List.filter(c=>c.MachineId == res.List[j].MachineId)[0];
                        if(item){
                            item.Connected = res.List[j].Connected;
                            item.Version = res.List[j].Version;
                            item.LastSignIn = res.List[j].LastSignIn;
                            item.Args = res.List[j].Args;
                            item.showTunnel = machineId.value != res.List[j].MachineId;
                            item.showForward = machineId.value != res.List[j].MachineId;
                            item.showSForward = machineId.value == res.List[j].MachineId;
                            item.showDel = machineId.value != res.List[j].MachineId && res.List[j].Connected == false;
                            item.isSelf = machineId.value == res.List[j].MachineId;
                        }
                    }
                    state.timer = setTimeout(_getSignList1, 5000);
                }).catch((err) => { 
                    state.timer =  setTimeout(_getSignList1, 5000);
                });
            }else{
                state.timer = setTimeout(_getSignList1, 5000);
            }
        }
        
        const handleDeviceEdit = (row)=>{
            state.deviceInfo = row;
            state.showDeviceEdit = true;
        }
        const handlePageRefresh = ()=>{
            handlePageChange();
            refreshTunnel();
            refreshTuntap();
            ElMessage.success('刷新成功');  
        }
        const handlePageChange = (page) => {
            if(page){
                state.page.Request.Page = page;
                _getSignList();
            }
        }
        const handleDel = (name) => {
            signInDel(name).then(() => {
                _getSignList();
            });
        }


        const timerState = reactive({
            timerTestTarget:0,
            timerTestListen:0,
            timerTestLocal:0,
        })
        const _testTargetForwardInfo = ()=>{
            testTargetForwardInfo(forward.value.current).then((res)=>{
                timerState.timerTestTarget = setTimeout(_testTargetForwardInfo,5000);
            }).catch(()=>{
                timerState.timerTestTarget = setTimeout(_testTargetForwardInfo,5000);
            });
        }
        const _testListenForwardInfo = ()=>{
            testListenForwardInfo(forward.value.current).then((res)=>{
                timerState.timerTestListen = setTimeout(_testListenForwardInfo,5000);
            }).catch(()=>{
                timerState.timerTestListen = setTimeout(_testListenForwardInfo,5000);
            });
        }
        const _testLocalSForwardInfo = ()=>{
            testLocalSForwardInfo().then((res)=>{
                timerState.timerTestLocal = setTimeout(_testLocalSForwardInfo,5000);
            }).catch(()=>{
                timerState.timerTestLocal = setTimeout(_testLocalSForwardInfo,5000);
            });
        }

        onMounted(() => {
            subWebsocketState((state) => { 
                if (state){
                    handlePageChange();
                    _getSignList();
                } 
            });
            _getSignList();
            _getSignList1();
            _getTuntapInfo();
            _getTunnelInfo();
            _getForwardConnections();
            _getTuntapConnections();
            _getForwardInfo();
            _getSForwardInfo();

            _testTargetForwardInfo();
            _testListenForwardInfo();
            _testLocalSForwardInfo();
        });
        onUnmounted(() => {
            clearTimeout( state.timer);
            clearTimeout(tuntap.value.timer);
            clearTimeout(tunnel.value.timer);
            clearTimeout(forward.value.timer);
            clearTimeout(sforward.value.timer);

            clearTimeout(timerState.timerTestTarget);
            clearTimeout(timerState.timerTestListen);
            clearTimeout(timerState.timerTestLocal);
        });

        return {
            machineId, state, 
            handleDeviceEdit,handlePageRefresh, handlePageChange, handleDel,
            tuntap, handleTuntapEdit, handleTuntapChange, handleTuntapRefresh,
            tunnel,connections, handleTunnelEdit, handleTunnelChange, handleTunnelRefresh,handleTunnelConnections,
            forward,sforward, handleForwardEdit,handleForwardChange,handleForwardRefresh,handleSForwardEdit,handleSForwardChange
        }
    }
}
</script>
<style lang="stylus" scoped>
.home-list-wrap{
    padding:1rem;

    .green{color:green;}

    .page{padding-top:1rem}
    .page-wrap{
        display:inline-block;
    }
}
</style>